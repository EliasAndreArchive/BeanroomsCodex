using System.Numerics;
using Beanrooms;

// Headless checks for the systems retained in the lean-core branch.
static void Check(bool condition,string message)
{
    if(!condition) throw new Exception(message);
}
var menu=new DebugMenu();
menu.ShowTitle();
menu.MouseDown(55,46);
menu.MouseMove(320,240);
menu.MouseUp();
Check(menu.WindowX==90 && menu.WindowY==80,"Menu window did not stay inside the 320x240 grid.");
menu.MouseDown(130,166);
Check(menu.TitleScreen && menu.TitlePage==1,"Moved title window lost its button hit areas.");
menu.WindowX=45;menu.WindowY=40;
menu.ShowTitle();
int titleChoice=-1;
menu.TitleAction=choice=>titleChoice=choice;
menu.MouseDown(100,102);
Check(titleChoice==0,"Solo play did not activate from the title menu.");
menu.ShowTitle();
menu.MouseDown(100,122);
Check(menu.TitleScreen && menu.TitlePage==1,"Join screen did not open from the title menu.");
bool joinCancelled=false;
menu.CancelJoinAction=()=>joinCancelled=true;
menu.Back();
Check(joinCancelled && menu.TitlePage==0,"Backing out of online join did not cancel it.");
menu.ShowSettingsFromTitle();
menu.AdjustSelected(1);
Check(menu.Page==11,"Title settings did not open player display controls.");
menu.CyclePage(1);
Check(menu.Page==7,"Player image tab did not open post effects.");
menu.Selected=8;
menu.AdjustSelected(1);
Check(!menu.Dithering,"Image dither toggle did not change the render setting.");
menu.Selected=9;
menu.AdjustSelected(1);
Check(!menu.PaletteReduction,"Image posterization toggle did not change the render setting.");
menu.SetPage(11);
menu.Selected=0;
int oldRenderer=menu.RenderMode;
menu.AdjustSelected(1);
Check(menu.RenderMode!=oldRenderer,"Display menu did not expose renderer selection.");
menu.Back();
menu.LeaveTitle();
menu.Open=true;
menu.SetPage(9);
menu.Selected=3;
menu.AdjustSelected(1);
menu.AdjustSelected(1);
Check(menu.Page==1,"Developer entry did not open render tuning.");
menu.SetPage(2);
Check(menu.Page==1,"Removed movement settings are still reachable.");
menu.SetPage(4);
Check(menu.Page==1,"Removed object physics settings are still reachable.");
menu.SetPage(8);
Check(menu.Page==1,"Removed flicker settings are still reachable.");
menu.SetPage(5);
bool monsterBefore=menu.MonsterSettings.Enabled;
menu.Selected=0;
menu.AdjustSelected(1);
Check(menu.MonsterSettings.Enabled!=monsterBefore,"Developer monster toggle failed.");
menu.SetPage(7);
menu.Dithering=false;
menu.PaletteReduction=false;
menu.ResetDefaults();
Check(menu.Dithering && menu.PaletteReduction && menu.MonsterSettings.Enabled,
    "Player image reset changed developer monster behavior.");
menu.SetPage(5);
menu.ResetDefaults();
Check(!menu.MonsterSettings.Enabled,"Developer monster reset failed.");
menu.SetPage(9);
int sessionChoice=-1;
menu.SessionAction=choice=>sessionChoice=choice;
menu.Selected=5;
menu.AdjustSelected(1);
Check(sessionChoice==1,"Leave session did not route from the pause menu.");
menu.Selected=6;
menu.AdjustSelected(1);
Check(sessionChoice==2,"Pause menu exit did not route to quit game.");
menu.Select(1);
Check(menu.Selected==6,"Pause menu still has a hidden main-menu row.");
using(var frame=menu.Draw())
    Check(frame.Width==320 && frame.Height==240,"Menu rendering size changed.");
if(args.Length==2 && args[0]=="--menu-preview")
{
    Directory.CreateDirectory(args[1]);
    menu.ShowTitle();
    using(var title=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"title.bgra"),title.Pixels);
    menu.ShowSettingsFromTitle();
    menu.AdjustSelected(1);
    using(var display=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"display.bgra"),display.Pixels);
    menu.LeaveTitle();menu.Open=true;menu.SetPage(9);
    using(var pause=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"pause.bgra"),pause.Pixels);
    menu.OpenCategories(developer:true);menu.SetPage(10);
    using(var diagnostics=menu.Draw()) File.WriteAllBytes(Path.Combine(args[1],"diagnostics.bgra"),diagnostics.Pixels);
}

var layout=WorldLayout.Generate(82317);
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

using(var authority=new PhotonAuthority([4,9]))
{
    authority.Update(.06f);
    NetMessage? initial=authority.DrainOutgoing().LastOrDefault(item=>item.Message.Type=="snapshot").Message;
    Check(initial?.HostId==4 && initial.Players.Length==2,"Photon authority did not assign host and players.");
    Check(initial?.Cubes.Length==2 && initial.Cubes.All(cube=>cube.HalfSize>0),
        "Photon room did not place its two playable cubes.");
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

Console.WriteLine("PASS: player/developer menus, clear spawn, cube physics, Photon authority, solo mode");
