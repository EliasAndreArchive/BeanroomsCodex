using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Beanrooms;

internal sealed class LocalNetClient : IDisposable
{
    readonly string host;
    readonly int port;
    readonly Channel<NetMessage> outgoing = Channel.CreateUnbounded<NetMessage>(
        new UnboundedChannelOptions { SingleReader = true });
    readonly Channel<byte> sendWake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    NetMessage? latestState;
    readonly object stateLock = new();
    readonly CancellationTokenSource stop = new();
    TcpClient? socket;
    ClientWebSocket? webSocket;
    NetMessage? pendingWorld;
    NetMessage? pendingRespawn;
    NetMessage? pendingPickupResult;
    readonly Queue<CubeImpactEvent> pendingCubeImpacts = new();
    int pendingFlicker;
    PlayerState[] players = [];
    CubeState[] cubes = [];
    MonsterState? monster;
    MonsterTuning? monsterTuning;
    FlickerTuning? flickerTuning;
    SandboxDevice[] devices=[];
    SandboxWire[] wires=[];
    bool sandboxEnabled;
    int id, connected, hostId;
    string status="OFFLINE";

    internal LocalNetClient(string host = "127.0.0.1", int port = 27015)
    {
        this.host = host;
        this.port = port;
    }

    internal bool Connected => Volatile.Read(ref connected) != 0;
    internal string Status => Volatile.Read(ref status);
    internal int Id => Volatile.Read(ref id);
    internal void Start() { Volatile.Write(ref status,"CONNECTING"); _ = Task.Run(RunAsync); }

    async Task RunAsync()
    {
        if(Uri.TryCreate(host,UriKind.Absolute,out Uri? uri) && uri.Scheme is "ws" or "wss")
        {
            while(!stop.IsCancellationRequested)
            {
                try
                {
                    Volatile.Write(ref status,"CONNECTING");
                    using var connection=new ClientWebSocket();
                    webSocket=connection;
                    await connection.ConnectAsync(uri,stop.Token);
                    using var session=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    Task sender=SendWebSocketLoop(connection,session.Token);
                    try
                    {
                        var buffer=new byte[16384];
                        using var messageBuffer=new MemoryStream();
                        while(!stop.IsCancellationRequested && connection.State==WebSocketState.Open)
                        {
                            WebSocketReceiveResult result=await connection.ReceiveAsync(new ArraySegment<byte>(buffer),stop.Token);
                            if(result.MessageType==WebSocketMessageType.Close) break;
                            if(result.MessageType!=WebSocketMessageType.Text) continue;
                            messageBuffer.Write(buffer,0,result.Count);
                            if(messageBuffer.Length>4_000_000) throw new InvalidDataException("Server message too large");
                            if(!result.EndOfMessage) continue;
                            ReceiveMessage(Encoding.UTF8.GetString(messageBuffer.GetBuffer(),0,(int)messageBuffer.Length));
                            messageBuffer.SetLength(0);
                        }
                    }
                    finally
                    {
                        session.Cancel();
                        try { await sender; }
                        catch(OperationCanceledException) { }
                        catch(WebSocketException) { }
                    }
                }
                catch(OperationCanceledException) when(stop.IsCancellationRequested) { break; }
                catch(Exception ex)
                {
                    Volatile.Write(ref status,ex.Message.Contains("401",StringComparison.Ordinal) ||
                        ex.Message.Contains("403",StringComparison.Ordinal) ? "KEY REJECTED" : "SERVER UNAVAILABLE");
                } // Retry after a Render cold start or restart.
                finally
                {
                    if(Connected) Volatile.Write(ref status,"RECONNECTING");
                    ResetConnectionState();
                    webSocket=null;
                    while(outgoing.Reader.TryRead(out _)) { }
                    Interlocked.Exchange(ref latestState,null);
                    while(sendWake.Reader.TryRead(out _)) { }
                }
                try { await Task.Delay(3000,stop.Token); }
                catch(OperationCanceledException) { break; }
            }
            return;
        }
        try
        {
            socket = new TcpClient { NoDelay = true };
            await socket.ConnectAsync(host, port, stop.Token);
            using NetworkStream stream = socket.GetStream();
            using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true };
            Task sender = SendLoop(writer);
            while (!stop.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(stop.Token);
                if (line == null) break;
                ReceiveMessage(line);
            }
            outgoing.Writer.TryComplete();
            sendWake.Writer.TryComplete();
            await sender;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { } // The standalone game still runs when no local server is open.
        finally
        {
            ResetConnectionState();
            socket?.Dispose();
        }
    }

    void ResetConnectionState()
    {
        Volatile.Write(ref connected,0);
        Volatile.Write(ref id,0);
        lock(stateLock)
        {
            players=[];cubes=[];monster=null;monsterTuning=null;flickerTuning=null;
            hostId=0;pendingWorld=null;pendingRespawn=null;pendingPickupResult=null;
            pendingCubeImpacts.Clear();
        }
        Interlocked.Exchange(ref pendingFlicker,0);
    }

    void ReceiveMessage(string line)
    {
        NetMessage? message=JsonSerializer.Deserialize<NetMessage>(line);
        if(message==null) return;
        if(message.Type=="welcome")
        {
            Volatile.Write(ref status,"ONLINE");
            Volatile.Write(ref id,message.Id);
            lock(stateLock) pendingWorld=message;
            Volatile.Write(ref connected,1);
        }
        else if(message.Type=="world") { lock(stateLock) pendingWorld=message; }
        else if(message.Type=="respawn") { lock(stateLock) pendingRespawn=message; }
        else if(message.Type=="pickup_result") { lock(stateLock) pendingPickupResult=message; }
        else if(message.Type=="flicker") Interlocked.Exchange(ref pendingFlicker,1);
        else if(message.Type=="snapshot")
        {
            lock(stateLock)
            {
                players=message.Players;cubes=message.Cubes;monster=message.Monster;
                monsterTuning=message.MonsterTuning;flickerTuning=message.FlickerTuning;
                hostId=message.HostId;
                devices=message.Devices;wires=message.Wires;sandboxEnabled=message.SandboxEnabled;
                foreach(CubeImpactEvent impact in message.CubeImpacts) pendingCubeImpacts.Enqueue(impact);
                while(pendingCubeImpacts.Count>128) pendingCubeImpacts.Dequeue();
            }
        }
    }

    async Task SendWebSocketLoop(ClientWebSocket connection,CancellationToken cancel)
    {
        while(await sendWake.Reader.WaitToReadAsync(cancel))
        {
            while(sendWake.Reader.TryRead(out _)) { }
            NetMessage? state=Interlocked.Exchange(ref latestState,null);
            if(state!=null) await SendWebSocketMessage(connection,state,cancel);
            while(outgoing.Reader.TryRead(out NetMessage? control))
                await SendWebSocketMessage(connection,control,cancel);
        }
    }

    static async Task SendWebSocketMessage(ClientWebSocket connection,NetMessage message,CancellationToken cancel)
    {
        byte[] bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await connection.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,cancel);
    }

    async Task SendLoop(StreamWriter writer)
    {
        while(await sendWake.Reader.WaitToReadAsync(stop.Token))
        {
            while(sendWake.Reader.TryRead(out _)) { }
            NetMessage? state=Interlocked.Exchange(ref latestState,null);
            if(state!=null) await writer.WriteLineAsync(JsonSerializer.Serialize(state));
            while(outgoing.Reader.TryRead(out NetMessage? control))
                await writer.WriteLineAsync(JsonSerializer.Serialize(control));
        }
    }

    internal void Send(NetMessage message)
    {
        if (!Connected) return;
        if(message.Type=="state") Interlocked.Exchange(ref latestState,message);
        else outgoing.Writer.TryWrite(message);
        sendWake.Writer.TryWrite(0);
    }

    internal NetMessage? ConsumeWorld()
    {
        lock(stateLock)
        {
            NetMessage? result = pendingWorld;
            pendingWorld = null;
            return result;
        }
    }

    internal NetMessage? ConsumeRespawn()
    {
        lock(stateLock)
        {
            NetMessage? result=pendingRespawn;
            pendingRespawn=null;
            return result;
        }
    }

    internal bool ConsumeFlicker() => Interlocked.Exchange(ref pendingFlicker,0)!=0;

    internal NetMessage? ConsumePickupResult()
    {
        lock(stateLock)
        {
            NetMessage? result=pendingPickupResult;
            pendingPickupResult=null;
            return result;
        }
    }

    internal CubeImpactEvent[] ConsumeCubeImpacts()
    {
        lock(stateLock)
        {
            CubeImpactEvent[] events=pendingCubeImpacts.ToArray();
            pendingCubeImpacts.Clear();
            return events;
        }
    }

    internal (PlayerState[] Players, CubeState[] Cubes, MonsterState? Monster,
        MonsterTuning? MonsterTuning,FlickerTuning? FlickerTuning,int HostId,
        SandboxDevice[] Devices,SandboxWire[] Wires,bool SandboxEnabled) Snapshot()
    {
        lock(stateLock) return (players, cubes, monster, monsterTuning, flickerTuning,hostId,devices,wires,sandboxEnabled);
    }

    public void Dispose()
    {
        stop.Cancel();
        outgoing.Writer.TryComplete();
        sendWake.Writer.TryComplete();
        socket?.Dispose();
        webSocket?.Dispose();
        stop.Dispose();
    }
}
