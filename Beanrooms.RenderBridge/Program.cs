using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;

string joinKey=Environment.GetEnvironmentVariable("BEANROOMS_JOIN_KEY") ?? "";
if(string.IsNullOrWhiteSpace(joinKey)) throw new InvalidOperationException("BEANROOMS_JOIN_KEY must be set.");
int port=int.TryParse(Environment.GetEnvironmentVariable("PORT"),out int configuredPort)?configuredPort:10000;
var builder=WebApplication.CreateBuilder(args);
// ASP.NET's default request log includes the WebSocket URL, including its key.
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics",LogLevel.Warning);
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
var app=builder.Build();
long nextSessionId=0;
app.UseWebSockets();
app.MapGet("/health",()=>Results.Ok("Beanrooms bridge ready"));
app.MapGet("/ready",async (CancellationToken cancel)=>
{
    using var probe=CancellationTokenSource.CreateLinkedTokenSource(cancel);
    probe.CancelAfter(TimeSpan.FromSeconds(12));
    try
    {
        using var tcp=new TcpClient { NoDelay=true };
        await tcp.ConnectAsync("127.0.0.1",27016,probe.Token);
        using var reader=new StreamReader(tcp.GetStream(),Encoding.UTF8);
        string? first=await reader.ReadLineAsync(probe.Token);
        return first=="READY"
            ? Results.Ok("Beanrooms server ready")
            : Results.StatusCode(503);
    }
    catch(Exception ex) when(ex is SocketException or OperationCanceledException or IOException)
    { return Results.StatusCode(503); }
});
app.Map("/ws",async context=>
{
    string supplied=context.Request.Query["key"].ToString();
    byte[] expected=Encoding.UTF8.GetBytes(joinKey), actual=Encoding.UTF8.GetBytes(supplied);
    if(actual.Length!=expected.Length || !CryptographicOperations.FixedTimeEquals(actual,expected))
    { context.Response.StatusCode=401;return; }
    if(!context.WebSockets.IsWebSocketRequest)
    { context.Response.StatusCode=400;return; }

    using var tcp=new TcpClient { NoDelay=true };
    try { await tcp.ConnectAsync("127.0.0.1",27015,context.RequestAborted); }
    catch(SocketException) { context.Response.StatusCode=503;return; }
    long sessionId=Interlocked.Increment(ref nextSessionId);
    var lifetime=Stopwatch.StartNew();
    Console.WriteLine($"WebSocket session {sessionId} connected to dedicated server.");
    using WebSocket webSocket=await context.WebSockets.AcceptWebSocketAsync();
    using NetworkStream stream=tcp.GetStream();
    using var reader=new StreamReader(stream,Encoding.UTF8,leaveOpen:true);
    using var writer=new StreamWriter(stream,new UTF8Encoding(false),leaveOpen:true) { AutoFlush=true };
    using var session=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    Task toServer=CopyWebSocketToServer(webSocket,writer,session.Token);
    Task toClient=CopyServerToWebSocket(reader,webSocket,session.Token);
    try
    {
        Task finished=await Task.WhenAny(toServer,toClient);
        string direction=finished==toServer ? "client to server" : "server to client";
        string result=finished.IsFaulted ? finished.Exception?.GetBaseException().GetType().Name ?? "error" :
            finished.IsCanceled ? "canceled" : "closed";
        Console.WriteLine($"WebSocket session {sessionId} ended after {lifetime.Elapsed.TotalSeconds:0.0}s ({direction}: {result}).");
    }
    finally
    {
        session.Cancel();
        webSocket.Abort();
        tcp.Close();
        try { await Task.WhenAll(toServer,toClient); }
        catch(OperationCanceledException) { }
        catch(WebSocketException) { }
        catch(IOException) { }
    }
});
app.Run();

static async Task CopyWebSocketToServer(WebSocket webSocket,StreamWriter writer,CancellationToken stop)
{
    var buffer=new byte[16384];
    using var message=new MemoryStream();
    while(!stop.IsCancellationRequested)
    {
        WebSocketReceiveResult result=await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer),stop);
        if(result.MessageType==WebSocketMessageType.Close) break;
        if(result.MessageType!=WebSocketMessageType.Text) continue;
        message.Write(buffer,0,result.Count);
        if(message.Length>4_000_000) throw new InvalidDataException("Client message too large");
        if(!result.EndOfMessage) continue;
        await writer.WriteLineAsync(Encoding.UTF8.GetString(message.GetBuffer(),0,(int)message.Length));
        message.SetLength(0);
    }
}

static async Task CopyServerToWebSocket(StreamReader reader,WebSocket webSocket,CancellationToken stop)
{
    while(!stop.IsCancellationRequested)
    {
        string? line=await reader.ReadLineAsync(stop);
        if(line==null) break;
        if(line.Contains("\"Type\":\"welcome\"",StringComparison.Ordinal))
            Console.WriteLine("Beanrooms bridge received player welcome.");
        byte[] bytes=Encoding.UTF8.GetBytes(line);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes),WebSocketMessageType.Text,true,stop);
    }
}
