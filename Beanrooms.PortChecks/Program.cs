using Beanrooms;
using StbImageSharp;

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
Console.WriteLine("PASS: menu, Tab toggle, textures, eight audio clips, Bepu physics, online address");
if(args.Contains("--network") || args.Contains("--websocket"))
{
    bool webSocket=args.Contains("--websocket");
    using var client=webSocket
        ? new LocalNetClient("ws://127.0.0.1:10000/ws?key=port-test-key",10000)
        : new LocalNetClient("127.0.0.1",27075);
    client.Start();
    var deadline=DateTime.UtcNow.AddSeconds(8);
    while(!client.Connected && DateTime.UtcNow<deadline) Thread.Sleep(25);
    if(!client.Connected || client.Id<=0 || client.ConsumeWorld()?.Type!="welcome")
        throw new Exception("Local multiplayer handshake failed: "+client.Status);
    Console.WriteLine($"PASS: {(webSocket?"WebSocket":"local TCP")} multiplayer welcome and world state");
}
