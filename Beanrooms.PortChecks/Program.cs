using System.Numerics;
using Beanrooms;

// Headless checks for terminal commands and retained gameplay systems.
static void Check(bool condition,string message)
{
    if(!condition) throw new Exception(message);
}
static void Send(DebugMenu menu,string command)
{
    foreach(char c in command) menu.TypeCommandCharacter(c);
    menu.SubmitCommand();
}
static void ClickLine(DebugMenu menu,string prefix,int column=-1)
{
    menu.FlushTerminal();
    IReadOnlyList<string> history=menu.TerminalHistory;
    int top=Math.Max(0,history.Count-17-menu.TerminalScroll);
    int index=-1;
    for(int i=top;i<history.Count && i<top+17;i++)
        if(history[i].StartsWith(prefix,StringComparison.Ordinal)) index=i;
    Check(index>=top,$"Visible menu line missing: {prefix}");
    menu.ClickTerminal(10+Math.Max(0,column)*6+2,10+(index-top)*12+3);
}
static void ClickWideRow(DebugMenu menu,string prefix)
{
    menu.FlushTerminal();
    IReadOnlyList<string> history=menu.TerminalHistory;
    int top=Math.Max(0,history.Count-17-menu.TerminalScroll);
    int index=-1;
    for(int i=top;i<history.Count && i<top+17;i++)
        if(history[i].StartsWith(prefix,StringComparison.Ordinal)) index=i;
    Check(index>=top,$"Visible menu line missing: {prefix}");
    menu.ClickTerminal(300,10+(index-top)*12+11);
}
var menu=new DebugMenu();
menu.ShowTitle();
int outputSounds=0,inputSounds=0;
menu.OutputSound=()=>outputSounds++;
menu.InputSound=()=>inputSounds++;
Check(menu.RevealedCharacters==0,"Terminal output appeared without being printed.");
menu.AdvanceTerminal(.05f);
Check(menu.RevealedCharacters>0 &&
    menu.RevealedCharacters<menu.TerminalHistory.Sum(line=>line.Length),
    "Terminal output did not write progressively.");
Check(outputSounds==menu.RevealedCharacters,"Each printed character needs its own sound.");
menu.FlushTerminal();
menu.TypeCommandCharacter('S');
Check(inputSounds==1,"Typed input did not trigger one sound per character.");
menu.BackspaceCommand();
Check(menu.CommandLine.Length==0,"Command backspace failed.");
ClickWideRow(menu,"ONLINE");
Check(menu.TitleScreen && menu.TitlePage==1,"ONLINE did not open room joining.");
bool connectStarted=false;
menu.ConnectAction=()=>connectStarted=true;
Send(menu,"CONNECT BEAN");
Check(menu.JoinKey=="BEAN" && connectStarted,"CONNECT BEAN did not join the named room.");
bool joinCancelled=false;
menu.CancelJoinAction=()=>joinCancelled=true;
Send(menu,"BACK");
Check(joinCancelled && menu.TitlePage==0,"BACK did not cancel room joining.");
Send(menu,"SETTINGS");
Check(menu.SettingsFromTitle && menu.View==DebugMenu.MenuView.Categories,
    "SETTINGS did not open from the main menu.");
Check(menu.TerminalHistory.Any(line=>line.Contains("/BEANROOMS/MAINMENU> SETTINGS")),
    "Entering settings lost the main menu prompt from scrollback.");
menu.ScrollTerminal(100);
Check(menu.TerminalScroll>0,"Terminal scrollback cannot reach the main menu.");
menu.ScrollTerminal(-100);
Send(menu,"DISPLAY");
Check(menu.Page==11,"DISPLAY did not open player display settings.");
Check(menu.TerminalHistory.Any(line=>line.StartsWith("LIGHTING") && line.Contains("<PATH TRACED>")),
    "Renderer did not show its current choice.");
ClickLine(menu,"LIGHTING");
Check(menu.RenderMode==1,"Renderer command did not select realtime lighting.");
Check(menu.TerminalHistory.Any(line=>line.StartsWith("LIGHTING") && line.Contains("<REALTIME>")),
    "Clicked choice did not update the displayed value.");
Send(menu,"BACK");
Send(menu,"IMAGE");
Check(menu.Page==7,"IMAGE did not open image settings.");
int tonemapBefore=menu.TonemapMode;
string exposureLine=menu.TerminalHistory.Last(line=>line.StartsWith("EXPOSURE "));
Check(!exposureLine.Contains('['),"Exposure should use a drawn slider rather than text art.");
ClickLine(menu,"EXPOSURE ",DebugMenu.SliderStartColumn);
menu.DragTerminalSlider(10+DebugMenu.SliderEndColumn*6+2);
menu.ReleaseTerminalSlider();
Check(Math.Abs(menu.Exposure-3f)<.01f,"Exposure slider did not drag to maximum.");
Send(menu,"SET EXPOSURE 2.25");
Check(Math.Abs(menu.Exposure-2.25f)<0.01f && menu.TonemapMode==tonemapBefore,
    "Image exposure command changed tonemap or missed the requested value.");
Send(menu,"SET EXPOSURE 9999");
Check(Math.Abs(menu.Exposure-2.25f)<0.01f && menu.CommandFeedback=="VALUE NOT AVAILABLE",
    "Invalid setting value changed exposure.");
ClickLine(menu,"DITHER ");
Check(!menu.Dithering,"Dither command did not toggle.");
Send(menu,"POSTERIZATION");
Check(!menu.PaletteReduction,"Posterization command did not toggle.");
Send(menu,"BACK");
Send(menu,"HUD");
Check(menu.Page==12,"HUD did not open from settings.");
Send(menu,"BACK");
Send(menu,"PROFILE");
Check(menu.Page==6,"PROFILE did not open from settings.");
Send(menu,"NAME RED BEAN");
Check(menu.PlayerName=="RED BEAN","NAME command did not change the player name.");
Send(menu,"BACK");
Send(menu,"BACK");
Check(menu.TitleScreen && menu.TitlePage==0,"BACK did not return to the main menu.");
int titleChoice=-1;
menu.TitleAction=choice=>titleChoice=choice;
Send(menu,"SOLO");
Check(titleChoice==0,"SOLO command did not start local play.");
menu.LeaveTitle();
menu.Open=true;
menu.SetPage(9);
Send(menu,"PROFILE");
Check(menu.View==DebugMenu.MenuView.Settings,"PROFILE did not open from pause.");
Send(menu,"BACK");
Check(menu.View==DebugMenu.MenuView.Pause && menu.Page==9,
    "Returning from profile left the pause menu on a settings page.");
ClickWideRow(menu,"SETTINGS");
Check(menu.View==DebugMenu.MenuView.Categories,
    "Clicking SETTINGS after profile did not open the settings menu.");
Send(menu,"BACK");
ClickWideRow(menu,"SETTINGS");
Check(menu.View==DebugMenu.MenuView.Categories,
    "Repeated SETTINGS clicks did not show the menu.");
Send(menu,"BACK");
Send(menu,"NETWORK");
Check(menu.View==DebugMenu.MenuView.Pause && menu.CommandFeedback=="COMMAND NOT RECOGNIZED",
    "Network tab appeared in a solo session.");
menu.NetworkConnected=true;menu.NetworkId=1;menu.NetworkHost=1;menu.NetworkPlayers=2;
menu.UpdateNetworkRoom("BEAN",[
    new PlayerState { Id=1,Name="HOST BEAN",R=.9f,G=.8f,B=.7f },
    new PlayerState { Id=2,Name="BLUE BEAN",R=.2f,G=.4f,B=1f }
],1,1);
menu.PrintCurrentView();
ClickWideRow(menu,"NETWORK");
Check(menu.View==DebugMenu.MenuView.Network &&
    menu.TerminalHistory.Any(line=>line=="ROOM      BEAN") &&
    menu.TerminalHistory.Any(line=>line.StartsWith("HOST      HOST BEAN") && line.Contains("PLAYERS 2/4")) &&
    menu.TerminalHistory.Any(line=>line=="PLAYERS"),
    "Network tab did not show room code, player count, host and players command.");
Send(menu,"PLAYERS");
Check(menu.TerminalHistory.Any(line=>line=="P2  BLUE BEAN") &&
    menu.TerminalHistory.Any(line=>line=="P1  HOST BEAN  HOST  YOU"),
    "PLAYERS did not show the room roster.");
Send(menu,"BACK");
Check(menu.View==DebugMenu.MenuView.Network &&
    menu.TerminalHistory.Last(line=>line.Contains("DIR")).Contains("/BEANROOMS/NETWORK>"),
    "BACK from players did not return to chat.");
string sentChat="";
menu.ChatAction=text=>{ sentChat=text;return true; };
Send(menu,"hello, bean!");
Check(sentChat=="HELLO, BEAN!","Network input did not send a chat message.");
Send(menu,new string('A',101));
Check(menu.CommandFeedback=="MESSAGE TOO LONG" && sentChat=="HELLO, BEAN!",
    "Oversized chat was sent or silently truncated.");
menu.ReceiveNetworkChat(2,"hello, host!");
Check(menu.TerminalHistory.Any(line=>line=="BLUE BEAN> HELLO, HOST!"),
    "Chat did not show the sender name and message.");
menu.ReceiveNetworkChat(2,"THIS LONG MESSAGE CONTINUES ACROSS MULTIPLE ROWS SO THE CHAT CAN SHOW THE REST OF THE TEXT.");
Check(menu.TerminalHistory.Any(line=>line.StartsWith("BLUE BEAN> THIS LONG MESSAGE")) &&
    menu.TerminalHistory.Any(line=>line.StartsWith("  ") && line.Contains("THE TEXT.")),
    "Long chat messages did not wrap into readable rows.");
menu.FlushTerminal();
using(var networkFrame=menu.Draw())
{
    bool playerBlue=false;
    for(int pixel=0;pixel<networkFrame.Pixels.Length;pixel+=4)
        playerBlue|=networkFrame.Pixels[pixel]>230 && networkFrame.Pixels[pixel+2]<100;
    Check(playerBlue,"Chat player name was not rendered in the player's color.");
}
for(int i=0;i<14;i++) menu.ReceiveNetworkChat(2,$"MESSAGE {i}");
Check(menu.TerminalHistory.Any(line=>line=="BLUE BEAN> MESSAGE 13") &&
    !menu.TerminalHistory.Any(line=>line=="BLUE BEAN> MESSAGE 0"),
    "Network chat did not show its newest messages.");
menu.ScrollNetworkChat(8);
Check(menu.TerminalHistory.Any(line=>line=="BLUE BEAN> MESSAGE 0"),
    "Network chat could not scroll to older messages.");
Send(menu,"BACK");
Check(menu.View==DebugMenu.MenuView.Pause,"Network tab did not return to pause.");
menu.NetworkConnected=false;menu.ResetNetwork();
CubeSpawnSpec? spawnRequest=null;
menu.SpawnAction=spec=>{ spawnRequest=spec;return "SPAWNED"; };
Send(menu,"SPAWN CUBE 1 WHITE 2");
Check(spawnRequest is { Emissive:false, Size:1f, Amount:2 },
    "Global cube spawn command was not parsed.");
Check(menu.View==DebugMenu.MenuView.Pause,"Spawning unexpectedly opened a separate menu.");
Send(menu,"SPAWN GLOWCUBE 0.5 BLUE 1");
Check(spawnRequest is { Emissive:true, Size:0.5f, Amount:1 },
    "Global glowcube spawn command was not parsed.");
Send(menu,"SPAWN CUBE 9 WHITE 2");
Check(menu.CommandFeedback.StartsWith("SIZE MUST"),"Invalid cube size was accepted.");
Send(menu,"DEVELOPER");
Check(menu.View==DebugMenu.MenuView.Categories,"DEVELOPER command did not open advanced pages.");
Send(menu,"RENDER");
Check(menu.Page==1,"RENDER command did not open detailed renderer settings.");
Check(menu.TerminalHistory.Any(line=>line.StartsWith("RENDER MODE") && line.Contains('<')) &&
    menu.TerminalHistory.Any(line=>line.StartsWith("DIRECT SAMPLES ")),
    "Long settings page did not print every item.");
Check(menu.TerminalScroll>0,"Long settings page did not start at its first items.");
int firstItemsScroll=menu.TerminalScroll;
menu.ScrollTerminal(-100);
Check(menu.TerminalScroll==0,"Long settings page cannot scroll to its final items.");
menu.ScrollTerminal(firstItemsScroll);
menu.Samples=256;
menu.RefreshSettingLines();
ClickLine(menu,"PATH SAMPLES ");
Check(menu.Samples==16,"Named sample choices did not cycle from last to first.");
Send(menu,"SPAWN CUBE 1 WHITE 1");
Check(spawnRequest is { Emissive:false, Amount:1 } && menu.Page==1,
    "Spawn command did not work from a settings page.");
Send(menu,"NEXT");
Check(menu.CommandFeedback=="COMMAND NOT RECOGNIZED","Removed NEXT command is still active.");
Send(menu,"RESET");
Check(menu.CommandFeedback=="DEFAULTS RESTORED","RESET did not confirm defaults.");
menu.SetPage(2);
Check(menu.Page==1,"Removed movement settings are still reachable.");
menu.SetPage(4);
Check(menu.Page==1,"Removed object physics settings are still reachable.");
menu.SetPage(8);
Check(menu.Page==1,"Removed flicker settings are still reachable.");
Send(menu,"BACK");
Send(menu,"DIAGNOSTICS");
Check(menu.Page==10,"DIAGNOSTICS command did not open the debug page.");
Send(menu,"SYSTEM INFO");
Check(menu.ShowDebugOverlay,"Diagnostics command did not enable system info.");
Send(menu,"BACK");
Send(menu,"BACK");
int sessionChoice=-1;
menu.SessionAction=choice=>sessionChoice=choice;
Send(menu,"LEAVE");
Check(sessionChoice==1,"LEAVE did not route to the session exit.");
menu.ShowTitle();
Send(menu,"GIBBERISH");
Check(menu.CommandFeedback=="COMMAND NOT RECOGNIZED" &&
    menu.TerminalHistory.Any(line=>line=="ERROR: COMMAND NOT RECOGNIZED"),
    "Unknown command did not print an error.");
menu.FlushTerminal();
using(var errorFrame=menu.Draw())
{
    bool red=false,yellow=false;
    for(int pixel=0;pixel<errorFrame.Pixels.Length;pixel+=4)
    {
        byte blue=errorFrame.Pixels[pixel],green=errorFrame.Pixels[pixel+1],r=errorFrame.Pixels[pixel+2];
        red|=r>220 && green<140 && blue<140;
        yellow|=r>220 && green>180 && green<245 && blue<190;
    }
    Check(red && yellow,"Error and player input do not have distinct colors.");
}
Send(menu,"1");
Check(menu.CommandFeedback=="COMMAND NOT RECOGNIZED","Numeric menu selection is still accepted.");
Send(menu,"CLS");
Check(!menu.TerminalHistory.Any(line=>line.Contains("GIBBERISH")) &&
    menu.TerminalHistory.Any(line=>line=="SETTINGS"),
    "CLS did not clear history and redraw the current menu.");
ClickWideRow(menu,"SETTINGS");
Check(menu.View==DebugMenu.MenuView.Categories,"CLS left redrawn menu items unclickable.");
Send(menu,"CLEAR");
Check(menu.TerminalHistory.All(line=>!line.Contains("MAINMENU")),
    "CLEAR did not remove older menu transcript lines.");
Send(menu,"BACK");
using(var frame=menu.Draw())
    Check(frame.Width==320 && frame.Height==240,"Terminal rendering size changed.");
menu.LeaveTitle();menu.Open=true;menu.SetPage(9);menu.FlushTerminal();
using(var pauseFrame=menu.Draw())
    Check(pauseFrame.Pixels[3] is >200 and <255,"Gameplay terminal should be dark and translucent.");
if(args.Length==2 && args[0]=="--menu-preview")
{
    Directory.CreateDirectory(args[1]);
    menu.ShowTitle();
    menu.FlushTerminal();
    using(var title=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"title.bgra"),title.Pixels);
    Send(menu,"GIBBERISH");
    menu.FlushTerminal();
    using(var errorMenu=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"error.bgra"),errorMenu.Pixels);
    menu.ShowTitle();
    Send(menu,"SETTINGS");
    Send(menu,"DISPLAY");
    menu.FlushTerminal();
    using(var display=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"display.bgra"),display.Pixels);
    Send(menu,"BACK");
    Send(menu,"IMAGE");
    menu.FlushTerminal();
    using(var imageMenu=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"image.bgra"),imageMenu.Pixels);
    menu.LeaveTitle();menu.Open=true;menu.SetPage(9);
    menu.FlushTerminal();
    using(var pause=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"pause.bgra"),pause.Pixels);
    menu.NetworkConnected=true;menu.NetworkId=1;menu.NetworkHost=1;menu.NetworkPlayers=2;
    menu.UpdateNetworkRoom("BEAN",[
        new PlayerState { Id=1,Name="HOST BEAN",R=.9f,G=.8f,B=.7f },
        new PlayerState { Id=2,Name="BLUE BEAN",R=.2f,G=.4f,B=1f }
    ],1,1);
    Send(menu,"NETWORK");
    menu.ReceiveNetworkChat(2,"HELLO, HOST!");
    menu.ReceiveNetworkChat(1,"WELCOME TO BEANROOMS.");
    menu.ReceiveNetworkChat(2,"THIS MESSAGE WRAPS ACROSS CHAT ROWS SO THERE IS MORE ROOM TO TALK.");
    menu.FlushTerminal();
    using(var network=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"network.bgra"),network.Pixels);
    Send(menu,"PLAYERS");
    menu.FlushTerminal();
    using(var players=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"players.bgra"),players.Pixels);
    Send(menu,"BACK");
    Send(menu,"BACK");
    menu.NetworkConnected=false;menu.ResetNetwork();
    Send(menu,"DEVELOPER");
    Send(menu,"DIAGNOSTICS");
    menu.FlushTerminal();
    using(var diagnostics=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"diagnostics.bgra"),diagnostics.Pixels);
    Send(menu,"BACK");
    Send(menu,"RENDER");
    menu.FlushTerminal();
    using(var render=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"render.bgra"),render.Pixels);
}var layout=WorldLayout.Generate(82317);
Check(layout.Walls.Count>0,"Procedural layout has no walls.");
Vector2 spawn=WorldLayout.FindClearSpawn(layout.Walls,new Vector2(96,96),.4f);
Check(!layout.Walls.Any(wall=>spawn.X>wall.Left-.4f && spawn.X<wall.Right+.4f &&
    spawn.Y>wall.Top-.4f && spawn.Y<wall.Bottom+.4f),"Player spawn overlaps a wall.");

using(var physics=new PhysicsBodies())
{
    Vector3 eye=new(spawn.X,1.68f,spawn.Y);
    bool created=physics.Spawn(eye,Vector3.UnitZ,false,layout.Walls,eye,new Vector3(.92f),PhysicsMaterial.Wood);
    Check(created && physics.Bodies.Count==1,"Cube spawn failed.");
    Check(physics.Bodies[0].HalfSize>0,"Cube collision shape is invalid.");
}
Check(CubeSpawner.TryParse("cube 1 white 2",out CubeSpawnSpec parsed,out _)
    && parsed.Amount==2 && !parsed.Emissive,"Lowercase spawn example was rejected.");
using(var spawnedPhysics=new PhysicsBodies())
{
    Vector3 eye=new(10f,1.68f,10f);
    int count=CubeSpawner.Spawn(spawnedPhysics,[],eye,Vector3.UnitZ,parsed);
    Check(count==2 && spawnedPhysics.Bodies.All(cube=>Math.Abs(cube.HalfSize-.5f)<.001f),
        "Spawn command did not create two one-meter cubes.");
}

using(var authority=new PhotonAuthority([4,9]))
{
    authority.Update(.06f);
    NetMessage? initial=authority.DrainOutgoing().LastOrDefault(item=>item.Message.Type=="snapshot").Message;
    Check(initial?.HostId==4 && initial.Players.Length==2,"Photon authority did not assign host and players.");
    Check(initial?.Cubes.Length==2 && initial.Cubes.All(cube=>cube.HalfSize>0),
        "Photon room did not place its two playable cubes.");
    authority.Receive(4,new NetMessage { Type="spawn_cube",SpawnSize=1f,SpawnAmount=2,
        R=.92f,G=.92f,B=.92f });
    authority.Update(.06f);
    NetMessage? spawned=authority.DrainOutgoing().LastOrDefault(item=>item.Message.Type=="snapshot").Message;
    Check(spawned?.Cubes.Length>=3,"Online spawn request did not reach room authority.");
    var hostTuning=new MonsterTuning { Enabled=true };
    authority.Receive(4,new NetMessage { Type="state",MonsterTuning=hostTuning });
    authority.Update(.06f);
    NetMessage? host=authority.DrainOutgoing().LastOrDefault(item=>item.Message.Type=="snapshot").Message;
    Check(host?.MonsterTuning?.Enabled==true,"Host monster tuning was not accepted.");
    authority.Receive(9,new NetMessage { Type="state",MonsterTuning=new MonsterTuning { Enabled=false } });
    authority.Update(.06f);
    NetMessage? guest=authority.DrainOutgoing().LastOrDefault(item=>item.Message.Type=="snapshot").Message;
    Check(guest?.MonsterTuning?.Enabled==true,"Guest changed host-only monster tuning.");
}

using(var solo=new SoloNetClient())
{
    Check(!solo.Connected && solo.Snapshot().Players.Length==0,"Solo networking state is invalid.");
}

Console.WriteLine("PASS: terminal commands and scrollback, cube spawning, Photon authority, solo physics");
