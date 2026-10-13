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
var menu=new DebugMenu();
menu.ShowTitle();
Check(menu.RevealedCharacters==0,"Terminal output appeared without being printed.");
menu.AdvanceTerminal(.05f);
Check(menu.RevealedCharacters>0 &&
    menu.RevealedCharacters<menu.TerminalHistory.Sum(line=>line.Length),
    "Terminal output did not write progressively.");
menu.FlushTerminal();
menu.TypeCommandCharacter('S');
menu.BackspaceCommand();
Check(menu.CommandLine.Length==0,"Command backspace failed.");
Send(menu,"ONLINE");
Check(menu.TitleScreen && menu.TitlePage==1,"ONLINE did not open room joining.");
Send(menu,"ROOM TEST ROOM");
Check(menu.JoinKey=="TEST ROOM","ROOM command did not set the room name.");
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
Send(menu,"SET 1 REALTIME");
Check(menu.RenderMode==1,"Renderer command did not select realtime lighting.");
Send(menu,"BACK");
Send(menu,"IMAGE");
Check(menu.Page==7,"IMAGE did not open image settings.");
int tonemapBefore=menu.TonemapMode;
Send(menu,"SET 2 2.25");
Check(Math.Abs(menu.Exposure-2.25f)<0.01f && menu.TonemapMode==tonemapBefore,
    "Image exposure command changed tonemap or missed the requested value.");
Send(menu,"SET 2 9999");
Check(Math.Abs(menu.Exposure-2.25f)<0.01f && menu.CommandFeedback=="VALUE NOT AVAILABLE",
    "Invalid setting value changed exposure.");
Send(menu,"8");
Check(!menu.Dithering,"Dither command did not toggle.");
Send(menu,"9");
Check(!menu.PaletteReduction,"Posterization command did not toggle.");
Send(menu,"NEXT");
Check(menu.Page==12,"NEXT did not reach HUD settings.");
Send(menu,"NEXT");
Check(menu.Page==6,"NEXT did not reach player profile.");
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
CubeSpawnSpec? spawnRequest=null;
menu.SpawnAction=spec=>{ spawnRequest=spec;return "SPAWNED"; };
Send(menu,"SPAWN");
Check(menu.View==DebugMenu.MenuView.Spawn,"SPAWN did not open the object command menu.");
Send(menu,"CUBE 1 WHITE 2");
Check(spawnRequest is { Emissive:false, Size:1f, Amount:2 },
    "CUBE size color amount command was not parsed.");
Send(menu,"GLOWCUBE 0.5 BLUE 1");
Check(spawnRequest is { Emissive:true, Size:0.5f, Amount:1 },
    "GLOWCUBE command was not parsed.");
Send(menu,"CUBE 9 WHITE 2");
Check(menu.CommandFeedback.StartsWith("SIZE MUST"),"Invalid cube size was accepted.");
Send(menu,"BACK");
Check(menu.View==DebugMenu.MenuView.Pause,"BACK did not return from spawn.");
Send(menu,"DEVELOPER");
Check(menu.View==DebugMenu.MenuView.Categories,"DEVELOPER command did not open advanced pages.");
Send(menu,"RENDER");
Check(menu.Page==1,"RENDER command did not open detailed renderer settings.");
Send(menu,"MORE");
Check(menu.ScrollRow>0,"MORE did not scroll long settings pages.");
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
Send(menu,"4");
Check(menu.ShowDebugOverlay,"Diagnostics command did not enable system info.");
Send(menu,"BACK");
Send(menu,"BACK");
int sessionChoice=-1;
menu.SessionAction=choice=>sessionChoice=choice;
Send(menu,"LEAVE");
Check(sessionChoice==1,"LEAVE did not route to the session exit.");
menu.ShowTitle();
Send(menu,"GIBBERISH");
Check(menu.CommandFeedback.StartsWith("UNKNOWN"),"Unknown command gave no feedback.");
using(var frame=menu.Draw())
    Check(frame.Width==320 && frame.Height==240,"Terminal rendering size changed.");
menu.LeaveTitle();menu.Open=true;menu.SetPage(9);menu.FlushTerminal();
using(var pauseFrame=menu.Draw())
    Check(pauseFrame.Pixels[3] is >0 and <255,"Gameplay terminal is not translucent.");
if(args.Length==2 && args[0]=="--menu-preview")
{
    Directory.CreateDirectory(args[1]);
    menu.ShowTitle();
    menu.FlushTerminal();
    using(var title=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"title.bgra"),title.Pixels);
    Send(menu,"SETTINGS");
    Send(menu,"DISPLAY");
    menu.FlushTerminal();
    using(var display=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"display.bgra"),display.Pixels);
    menu.LeaveTitle();menu.Open=true;menu.SetPage(9);
    menu.FlushTerminal();
    using(var pause=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"pause.bgra"),pause.Pixels);
    Send(menu,"SPAWN");
    menu.FlushTerminal();
    using(var spawnMenu=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"spawn.bgra"),spawnMenu.Pixels);
    Send(menu,"BACK");
    Send(menu,"DEVELOPER");
    Send(menu,"DIAGNOSTICS");
    menu.FlushTerminal();
    using(var diagnostics=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"diagnostics.bgra"),diagnostics.Pixels);
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
