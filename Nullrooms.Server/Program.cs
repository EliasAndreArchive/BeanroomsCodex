using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Threading.Channels;

namespace Beanrooms;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        IPAddress listenAddress=IPAddress.Loopback;
        int port=27015;
        int healthPort=0;
        for(int i=0;i<args.Length;i++)
        {
            if(args[i]=="--listen" && i+1<args.Length && IPAddress.TryParse(args[++i],out IPAddress? address))
                listenAddress=address;
            else if(args[i]=="--port" && i+1<args.Length && int.TryParse(args[++i],out int chosenPort) && chosenPort is >=1 and <=65535)
                port=chosenPort;
            else if(args[i]=="--health-port" && i+1<args.Length && int.TryParse(args[++i],out int chosenHealthPort) && chosenHealthPort is >=1 and <=65535)
                healthPort=chosenHealthPort;
            else
            {
                Console.Error.WriteLine("Usage: Beanrooms.Server [--listen 0.0.0.0] [--port 27015] [--health-port 27016]");
                return 2;
            }
        }
        using var server = new LocalServer(listenAddress,port,healthPort);
        await server.RunAsync();
        return 0;
    }
}

internal sealed class LocalServer : IDisposable
{
    const float Step = 1f / 120f;
    readonly TcpListener listener;
    readonly TcpListener? healthListener;
    readonly ConcurrentDictionary<int, Peer> peers = new();
    readonly ConcurrentQueue<(int Id, NetMessage Message)> incoming = new();
    readonly PhysicsBodies physics = new();
    readonly MonsterTuning monsterSettings = new();
    readonly FlickerTuning flickerSettings = new();
    readonly CancellationTokenSource stop = new();
    readonly Stopwatch clock=Stopwatch.StartNew();
    List<RectangleF> walls = [];
    MonsterBrain? monster;
    int nextId, holderId, seed = 82317;
    double nextFlickerEvent,lastFlickerEvent=double.NegativeInfinity;

    internal LocalServer(IPAddress listenAddress,int port,int healthPort=0)
    {
        listener=new TcpListener(listenAddress,port);
        if(healthPort!=0) healthListener=new TcpListener(IPAddress.Loopback,healthPort);
        ResetWorld();
    }

    void ResetWorld()
    {
        physics.Clear();
        walls = WorldLayout.Generate(seed).Walls;
        monster = new MonsterBrain(seed,walls,monsterSettings);
        holderId=0;
    }

    Vector2 SpawnForPlayerSlot(int slot) =>
        WorldLayout.FindClearSpawn(walls,new Vector2(96f+slot*1.5f,96f),0.4f);

    internal async Task RunAsync()
    {
        try { listener.Start(); }
        catch(SocketException ex) when(ex.SocketErrorCode==SocketError.AddressAlreadyInUse)
        {
            Console.WriteLine($"A Beanrooms server is already using {listener.LocalEndpoint}.");
            await Task.Delay(2500);
            return;
        }
        Console.WriteLine($"Beanrooms server listening on {listener.LocalEndpoint}");
        if(healthListener!=null)
        {
            healthListener.Start();
            _=HealthAcceptLoop();
        }
        Console.WriteLine("Close this window or press Ctrl+C to stop the server.");
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel=true; stop.Cancel(); };
        _ = AcceptLoop();
        double previous=clock.Elapsed.TotalSeconds, accumulator=0, nextSnapshot=0;
        nextFlickerEvent=NextFlickerTime(previous);
        while(!stop.IsCancellationRequested)
        {
            double now=clock.Elapsed.TotalSeconds;
            accumulator=Math.Min(accumulator+Math.Clamp(now-previous,0,0.05),0.1);
            previous=now;
            ProcessMessages(now);
            if(flickerSettings.RandomEvents && now>=nextFlickerEvent) BroadcastLightEvent(now);
            while(accumulator>=Step)
            {
                Vector3 eye=new(96f,1.68f,96f), forward=Vector3.UnitZ;
                Quaternion rotation=Quaternion.Identity;
                if(holderId!=0 && peers.TryGetValue(holderId,out Peer? holder))
                {
                    eye=new Vector3(holder.X,holder.Y,holder.Z);
                    forward=LookDirection(holder.Yaw,holder.Pitch);
                    rotation=CameraOrientation.FromAngles(holder.Yaw,holder.Pitch);
                    physics.HoldDistance=holder.HoldDistance;
                    physics.Frequency=holder.SpringFrequency;
                    physics.Damping=holder.SpringDamping;
                    physics.TargetSmoothing=holder.PidSmoothing;
                    physics.SlideResistance=holder.SlideResistance;
                }
                // The server has no rigid-body work until the first cube is spawned.
                // Avoid building hundreds of Bepu wall colliders while an idle free
                // instance is trying to accept its first player.
                if(physics.Bodies.Count>0) physics.Update(Step,eye,forward,rotation,walls,eye);
                monster?.Update(Step,peers.Values.Select(peer=>new PlayerState
                {
                    Id=peer.Id,X=peer.X,Y=peer.Y,Z=peer.Z,Yaw=peer.Yaw
                }).ToArray(),physics.Bodies);
                CheckMonsterTouches(now);
                accumulator-=Step;
            }
            if(now>=nextSnapshot)
            {
                BroadcastSnapshot();
                nextSnapshot=now+0.05;
            }
            try { await Task.Delay(peers.IsEmpty && physics.Bodies.Count==0 ? 15 : 1,stop.Token); }
            catch(OperationCanceledException) { break; }
        }
    }

    async Task HealthAcceptLoop()
    {
        while(!stop.IsCancellationRequested)
        {
            TcpClient probe;
            try { probe=await healthListener!.AcceptTcpClientAsync(stop.Token); }
            catch(OperationCanceledException) { break; }
            catch(ObjectDisposedException) { break; }
            using(probe)
            {
                try
                {
                    await using NetworkStream stream=probe.GetStream();
                    await stream.WriteAsync("READY\n"u8.ToArray(),stop.Token);
                }
                catch(OperationCanceledException) when(stop.IsCancellationRequested) { break; }
                catch(IOException) { }
                catch(SocketException) { }
            }
        }
    }

    async Task AcceptLoop()
    {
        while(!stop.IsCancellationRequested)
        {
            TcpClient socket;
            try { socket=await listener.AcceptTcpClientAsync(stop.Token); }
            catch(OperationCanceledException) { break; }
            catch(ObjectDisposedException) { break; }
            if(peers.Count>=4) { socket.Dispose(); continue; }
            try
            {
                int id=Interlocked.Increment(ref nextId);
                Console.WriteLine($"Player {id} accepted; preparing welcome");
                var peer=new Peer(id,socket,incoming);
                Vector2 spawn=SpawnForPlayerSlot(peers.Count);
                peer.X=spawn.X;peer.Z=spawn.Y;
                peers[id]=peer;
                double age=clock.Elapsed.TotalSeconds-lastFlickerEvent;
                peer.Send(new NetMessage { Type="welcome",Id=id,Seed=seed,X=peer.X,Y=peer.Y,Z=peer.Z,
                    FlickerAge=age>=0 && age<flickerSettings.EventDuration ? (float)age : -1f });
                _=peer.RunAsync(stop.Token);
                Console.WriteLine($"Player {id} connected ({peers.Count} total)");
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"Could not accept player: {ex}");
                socket.Dispose();
            }
        }
    }

    static Vector3 LookDirection(float yaw,float pitch) => CameraOrientation.Forward(yaw,pitch);

    void CheckMonsterTouches(double now)
    {
        if(monster==null || !monsterSettings.Enabled) return;
        MonsterState eye=monster.State;
        foreach(Peer peer in peers.Values)
        {
            float dx=peer.X-eye.X,dz=peer.Z-eye.Z;
            if(now<peer.RespawnProtectedUntil || dx*dx+dz*dz>0.9f*0.9f) continue;
            Vector2 point=WorldLayout.FindSafeRespawn(walls,new Vector2(eye.X,eye.Z),0.4f,Random.Shared);
            peer.X=point.X;peer.Y=1.68f;peer.Z=point.Y;
            peer.RespawnId++;
            peer.RespawnProtectedUntil=now+2.5;
            if(holderId==peer.Id) { physics.Release();holderId=0; }
            peer.Send(new NetMessage { Type="respawn",RespawnId=peer.RespawnId,
                X=peer.X,Y=peer.Y,Z=peer.Z });
            Console.WriteLine($"Monster caught player {peer.Id}; respawned at {peer.X:0.0}, {peer.Z:0.0}");
        }
    }

    void BroadcastLightEvent(double now)
    {
        if(now-lastFlickerEvent<0.75) return;
        lastFlickerEvent=now;
        nextFlickerEvent=NextFlickerTime(now);
        foreach(Peer peer in peers.Values) peer.Send(new NetMessage { Type="flicker" });
    }

    double NextFlickerTime(double now) => now+
        flickerSettings.EventInterval*(0.67+Random.Shared.NextDouble()*0.66);

    void ProcessMessages(double now)
    {
        while(incoming.TryDequeue(out var entry))
        {
            if(!peers.TryGetValue(entry.Id,out Peer? peer)) continue;
            NetMessage message=entry.Message;
            switch(message.Type)
            {
                case "disconnect":
                    peers.TryRemove(entry.Id,out _);
                    if(holderId==entry.Id) { physics.Release(); holderId=0; }
                    Console.WriteLine($"Player {entry.Id} disconnected ({peers.Count} total)");
                    break;
                case "state":
                    if(float.IsFinite(message.X) && float.IsFinite(message.Y) && float.IsFinite(message.Z) &&
                       float.IsFinite(message.Yaw) && float.IsFinite(message.Pitch))
                    {
                        if(message.RespawnId==peer.RespawnId)
                        {
                            peer.X=Math.Clamp(message.X,4.3f,187.7f);
                            peer.Y=Math.Clamp(message.Y,0.8f,1.9f);
                            peer.Z=Math.Clamp(message.Z,4.3f,187.7f);
                        }
                        peer.Yaw=message.Yaw;
                        peer.Pitch=Math.Clamp(message.Pitch,-1.45f,1.45f);
                        string name=new string((message.Name??"").ToUpperInvariant()
                            .Where(c=>char.IsAsciiLetterOrDigit(c)||c==' '||c=='-').Take(12).ToArray()).Trim();
                        peer.Name=name.Length>0?name:$"BEAN{peer.Id}";
                        if(float.IsFinite(message.R)&&float.IsFinite(message.G)&&float.IsFinite(message.B))
                        {
                            peer.R=Math.Clamp(message.R,0.05f,1f);
                            peer.G=Math.Clamp(message.G,0.05f,1f);
                            peer.B=Math.Clamp(message.B,0.05f,1f);
                        }
                        peer.HoldDistance=Math.Clamp(message.HoldDistance,1.2f,4f);
                        if(message.PickupRange>0f) peer.PickupRange=Math.Clamp(message.PickupRange,1f,10f);
                        if(message.SpringFrequency>0f) peer.SpringFrequency=Math.Clamp(message.SpringFrequency,1f,30f);
                        if(message.SpringDamping>0f) peer.SpringDamping=Math.Clamp(message.SpringDamping,0.1f,5f);
                        if(message.PidSmoothing>0f) peer.PidSmoothing=Math.Clamp(message.PidSmoothing,1f,50f);
                        if(message.SlideResistance>0f) peer.SlideResistance=Math.Clamp(message.SlideResistance,0.1f,20f);
                    }
                    if(message.MonsterTuning!=null && peer.Id==peers.Keys.Min())
                    {
                        message.MonsterTuning.Clamp();
                        monsterSettings.CopyFrom(message.MonsterTuning);
                    }
                    if(message.FlickerTuning!=null && peer.Id==peers.Keys.Min())
                    {
                        message.FlickerTuning.Clamp();
                        if(flickerSettings.CopyFrom(message.FlickerTuning))
                            nextFlickerEvent=NextFlickerTime(now);
                    }
                    break;
                case "pickup":
                    if(holderId==0)
                    {
                        Vector3 eye=new(peer.X,peer.Y,peer.Z);
                        Quaternion rotation=CameraOrientation.FromAngles(peer.Yaw,peer.Pitch);
                        physics.PickupRange=peer.PickupRange;
                        physics.HoldDistance=peer.HoldDistance;
                        if(physics.TryPickup(eye,LookDirection(peer.Yaw,peer.Pitch),rotation,walls))
                            holderId=peer.Id;
                    }
                    break;
                case "release":
                    if(holderId==peer.Id) { physics.Release(); holderId=0; }
                    break;
                case "spawn":
                    {
                        Vector3 eye=new(peer.X,peer.Y,peer.Z);
                        Vector3 color=new(Math.Clamp(message.R,0f,1f),Math.Clamp(message.G,0f,1f),Math.Clamp(message.B,0f,1f));
                        physics.Spawn(eye,LookDirection(peer.Yaw,peer.Pitch),message.Action==1,walls,eye,color);
                        break;
                    }
                case "clear":
                    physics.Clear();
                    holderId=0;
                    break;
                case "regen":
                    if(peer.Id!=peers.Keys.Min()) break;
                    seed=Random.Shared.Next(1,1_000_000_000);
                    ResetWorld();
                    int slot=0;
                    foreach(Peer joined in peers.Values.OrderBy(player=>player.Id))
                    {
                        Vector2 spawn=SpawnForPlayerSlot(slot++);
                        joined.X=spawn.X;joined.Y=1.68f;joined.Z=spawn.Y;
                        joined.Send(new NetMessage { Type="world",Seed=seed,X=joined.X,Y=joined.Y,Z=joined.Z,
                            RespawnId=joined.RespawnId });
                    }
                    Console.WriteLine($"New room seed: {seed}");
                    break;
                case "flicker":
                    if(peer.Id==peers.Keys.Min()) BroadcastLightEvent(now);
                    break;
            }
        }
    }

    void BroadcastSnapshot()
    {
        PlayerState[] players=peers.Values.Select(peer=>new PlayerState
        {
            Id=peer.Id,X=peer.X,Y=peer.Y,Z=peer.Z,Yaw=peer.Yaw,
            Name=peer.Name,R=peer.R,G=peer.G,B=peer.B
        }).ToArray();
        CubeState[] cubes=physics.Bodies.Select(body=>new CubeState
        {
            Id=body.Id,
            X=body.Position.X,Y=body.Position.Y,Z=body.Position.Z,
            Qx=body.Rotation.X,Qy=body.Rotation.Y,Qz=body.Rotation.Z,Qw=body.Rotation.W,
            HalfSize=body.HalfSize,Emissive=body.Emissive,
            R=body.Color.X,G=body.Color.Y,B=body.Color.Z
        }).ToArray();
        var message=new NetMessage { Type="snapshot",Seed=seed,Players=players,Cubes=cubes,
            HostId=players.Select(player=>player.Id).DefaultIfEmpty(0).Min(),
            MonsterTuning=monsterSettings.Clone(),
            FlickerTuning=flickerSettings.Clone(),
            Monster=monster==null?null:new MonsterState
            {
                X=monster.State.X,Y=monster.State.Y,Z=monster.State.Z,Yaw=monster.State.Yaw,
                Mode=monster.State.Mode,TargetId=monster.State.TargetId,
                GoalX=monster.State.GoalX,GoalZ=monster.State.GoalZ,
                HasLastSeen=monster.State.HasLastSeen,LastX=monster.State.LastX,LastZ=monster.State.LastZ,
                Route=monster.State.Route
            } };
        foreach(Peer peer in peers.Values) peer.Send(message);
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        healthListener?.Stop();
        foreach(Peer peer in peers.Values) peer.Dispose();
        physics.Dispose();
        stop.Dispose();
    }
}

internal sealed class Peer : IDisposable
{
    readonly TcpClient socket;
    readonly ConcurrentQueue<(int Id, NetMessage Message)> incoming;
    readonly Channel<NetMessage> outgoing=Channel.CreateUnbounded<NetMessage>(
        new UnboundedChannelOptions { SingleReader=true });
    internal int Id { get; }
    internal float X=96f,Y=1.68f,Z=96f,Yaw=0.63f,Pitch,HoldDistance=2.25f;
    internal string Name;
    internal float R=0.78f,G=0.76f,B=0.70f;
    internal float PickupRange=5f,SpringFrequency=8.5f,SpringDamping=1.35f,PidSmoothing=18f,SlideResistance=4.5f;
    internal int RespawnId;
    internal double RespawnProtectedUntil;

    internal Peer(int id,TcpClient socket,ConcurrentQueue<(int,NetMessage)> incoming)
    {
        Id=id;Name=$"BEAN{id}";this.socket=socket;this.incoming=incoming;
        socket.NoDelay=true;
    }
    internal void Send(NetMessage message) => outgoing.Writer.TryWrite(message);

    internal async Task RunAsync(CancellationToken stop)
    {
        try
        {
            using NetworkStream stream=socket.GetStream();
            using var reader=new StreamReader(stream);
            using var writer=new StreamWriter(stream) { AutoFlush=true };
            Task sender=SendLoop(writer,stop);
            while(!stop.IsCancellationRequested)
            {
                string? line=await reader.ReadLineAsync(stop);
                if(line==null) break;
                NetMessage? message=JsonSerializer.Deserialize<NetMessage>(line);
                if(message!=null) incoming.Enqueue((Id,message));
            }
            outgoing.Writer.TryComplete();
            await sender;
        }
        catch(OperationCanceledException) { }
        catch(Exception) { }
        finally
        {
            incoming.Enqueue((Id,new NetMessage { Type="disconnect" }));
            Dispose();
        }
    }
    async Task SendLoop(StreamWriter writer,CancellationToken stop)
    {
        await foreach(NetMessage message in outgoing.Reader.ReadAllAsync(stop))
            await writer.WriteLineAsync(JsonSerializer.Serialize(message));
    }
    public void Dispose()
    {
        outgoing.Writer.TryComplete();
        socket.Dispose();
    }
}
