using Beanrooms;
using StbImageSharp;
using System.Drawing;
using System.Numerics;

var menu=new DebugMenu();
menu.ShowTitle();
using var image=menu.Draw();
if(image.Width!=320||image.Height!=240 || !image.Pixels.Where((_,i)=>i%4==3).Any(alpha=>alpha!=0))
    throw new Exception("The menu did not render into its 320x240 overlay");
foreach(string asset in new[]{"wall.png","floor.png","ceiling.png","beanface.png","eye.png"})
{
    string path=Path.Combine(AppContext.BaseDirectory,"assets",asset);
    var decoded=ImageResult.FromMemory(File.ReadAllBytes(path),ColorComponents.RedGreenBlueAlpha);
    if(decoded.Width==0 || decoded.Height==0) throw new Exception("Failed to decode "+asset);
}
using(var audio=new AudioSystem())
foreach(string asset in new[]{"spawn-boom.mp3","ui-click.wav","ui-hover.wav","lamp-buzz.mp3",
    "monster.wav","walking.mp3","running.wav","crouch-walking.mp3"})
{
    using var clip=audio.Load(asset);
    if(clip.Duration<=0) throw new Exception("Failed to decode "+asset);
}
using(var physics=new PhysicsBodies()) { }
var predicted=new HeldCubePrediction();
var testCube=new CubeState { Id=7,X=96,Y=1.5f,Z=98,HalfSize=0.43f,Qw=1 };
var testCubes=new[] { testCube };
Vector3 testEye=new(96,1.5f,96), testForward=Vector3.UnitZ;
if(!predicted.TryBegin(testCubes,testEye,testForward,Quaternion.Identity,5f,2.25f,[]))
    throw new Exception("Client could not predict a visible cube pickup");
predicted.Apply(1f/60f,testEye,testForward,Quaternion.Identity,2.25f,18f,[],testCubes,testCube);
if(Vector3.Distance(new Vector3(testCube.X,testCube.Y,testCube.Z),new Vector3(96,1.5f,98))>0.001f)
    throw new Exception("Predicted pickup moved the cube before the camera moved");
var blockingWall=new[] { new RectangleF(97f,97f,1f,2f) };
for(int i=0;i<60;i++)
    predicted.Apply(1f/60f,new Vector3(100,1.5f,96),testForward,
        Quaternion.Identity,2.25f,18f,blockingWall,testCubes,testCube);
if(testCube.X+testCube.HalfSize>97f)
    throw new Exception("Predicted carried cube crossed a wall");
predicted.Clear();
if(predicted.Active) throw new Exception("Released cube kept local prediction");
using(var game=new GameWindow())
{
    var key=typeof(GameWindow).GetMethod("ProcessCmdKey",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var menuField=typeof(GameWindow).GetField("menu",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var gameMenu=(DebugMenu)menuField.GetValue(game)!;
    if(gameMenu.Open) throw new Exception("Settings menu should start closed");
    key.Invoke(game,[new Message(),Keys.Tab]);
    if(!gameMenu.Open) throw new Exception("Tab did not open settings menu");
    key.Invoke(game,[new Message(),Keys.Tab]);
    if(gameMenu.Open) throw new Exception("Tab did not close settings menu");
}
if(!Beanrooms.Program.TryOnlineEndpoint("https://beanrooms-game.onrender.com","test-key",out string endpoint)
    || endpoint!="wss://beanrooms-game.onrender.com/ws?key=test-key")
    throw new Exception("Online server address format changed");
Console.WriteLine("PASS: menu, Tab toggle, textures, audio, Bepu physics, carried cube prediction, online address");
if(args.Contains("--network") || args.Contains("--websocket"))
{
    bool webSocket=args.Contains("--websocket");
    using var client=webSocket
        ? new LocalNetClient("ws://127.0.0.1:10000/ws?key=port-test-key",10000)
        : new LocalNetClient("127.0.0.1",27075);
    client.Start();
    var deadline=DateTime.UtcNow.AddSeconds(8);
    while(!client.Connected && DateTime.UtcNow<deadline) Thread.Sleep(25);
    NetMessage? welcome=client.ConsumeWorld();
    if(!client.Connected || client.Id<=0 || welcome?.Type!="welcome")
        throw new Exception("Local multiplayer handshake failed: "+client.Status);
    float finalX=welcome.X+0.25f;
    for(int i=0;i<100;i++)
        client.Send(new NetMessage { Type="state",X=welcome.X+i*0.0025f,Y=welcome.Y,
            Z=welcome.Z,RespawnId=welcome.RespawnId,HoldDistance=2.25f });
    client.Send(new NetMessage { Type="state",X=finalX,Y=welcome.Y,Z=welcome.Z,
        RespawnId=welcome.RespawnId,HoldDistance=2.25f });
    deadline=DateTime.UtcNow.AddSeconds(5);
    while(DateTime.UtcNow<deadline &&
          MathF.Abs((client.Snapshot().Players.FirstOrDefault(p=>p.Id==client.Id)?.X??0f)-finalX)>0.02f)
        Thread.Sleep(25);
    if(MathF.Abs((client.Snapshot().Players.FirstOrDefault(p=>p.Id==client.Id)?.X??0f)-finalX)>0.02f)
        throw new Exception("Latest player state did not reach the server");
    Console.WriteLine($"PASS: {(webSocket?"WebSocket":"local TCP")} multiplayer welcome and world state");
}
