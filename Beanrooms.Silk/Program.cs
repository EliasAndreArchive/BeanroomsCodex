using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beanrooms;

internal static class Program
{
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"crash.log"),e.ExceptionObject.ToString() ?? "Unknown error");
        using var game=new GameWindow();
        game.Run();
    }

}

internal enum GameSessionPhase { MainMenu, JoiningRoom, PlayingSolo, PlayingOnline }

internal sealed partial class GameWindow : SilkForm
{
    const int Grid = WorldLayout.GridSize;
    const int FixtureGrid = WorldLayout.GridSize/2+1;
    const float Cell = 4f;
    const float Radius = 0.4f;
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly HashSet<Keys> keys = new();
    readonly List<RectangleF> boxes = new();
    readonly List<Vector2> activeFixtures = new();
    readonly float[] fixturePixels = new float[FixtureGrid*FixtureGrid*4];
    readonly bool[] fixtureBase = new bool[FixtureGrid*FixtureGrid];
    readonly LightFlicker flicker;
    float[] grid = new float[Grid * Grid * 4];
    float[] heightGrid = new float[Grid * Grid * 4];
    float[] pitPixels = new float[(int)WorldLayout.WorldSize*WorldLayout.PitPixelsPerMeter*
        (int)WorldLayout.WorldSize*WorldLayout.PitPixelsPerMeter];
    WorldLayoutData? worldLayout;
    Vector3 camera = new(WorldLayout.Center, 1.68f, WorldLayout.Center);
    float yaw = 0.63f, pitch;
    float smoothYaw, smoothPitch;
    bool lookInitialized;
    int seed = 82317;
    int width = 320, height = 240;
    int frames, worldTexture, heightTexture, pitTexture, fixtureTexture, objectTexture, wallTexture, floorTexture, ceilingTexture, faceTexture, eyeTexture, overlayTexture;
    int pathProgram, probeProgram, displayProgram, menuBackgroundProgram, exposureProgram, bloomExtractProgram, bloomBlurProgram, framebuffer;
    int renderTexture, sceneTexture, bloomA, bloomB, exposureA, exposureB, exposureCurrent;
    int probeDirect, probeA, probeB, probeResult, probeSize;
    bool probeDirty = true;
    int lastProbeSettings;
    double lastExposureTime;
    bool active, wantsCapture = true, ready;
    readonly DebugMenu menu = new();
    readonly PlayerMotion motion = new();
    readonly SpawnTransition spawnTransition = new();
    GameAudio? audio;
    double lastSpawnSound=double.NegativeInfinity;
    readonly PhysicsBodies physics = new();
    readonly MenuBeanSimulation menuBeans = new();
    int[] cubeVisualIds = [];
    double[] cubeVisualBirth = [];
    float[] objectTexels = [];
    int objectTextureHeight;
    bool cubeVisualNetworkMode;
    INetClient network;
    bool networkStarted;
    GameSessionPhase sessionPhase=GameSessionPhase.MainMenu;
    bool HasPlayer => sessionPhase is GameSessionPhase.PlayingSolo or GameSessionPhase.PlayingOnline;
    readonly bool launchFullscreen;
    MonsterBrain? localMonster;
    MonsterState? visibleMonster;
    readonly Dictionary<int, Vector3> remotePositions = new();
    readonly Dictionary<int, float> remoteYaws = new();
    readonly Dictionary<int, Vector3> remoteColors = new();
    readonly Dictionary<int, string> remoteNames = new();
    readonly List<FootstepSource> remoteFootsteps = new();
    CubeState[] networkCubes = [];
    double nextNetworkSend;
    bool networkHolding;
    int networkHeldId;
    bool rotatingHeld;
    float grabYaw,grabPitch;
    int grabSequence;
    int respawnId;
    double localRespawnProtectedUntil;
    bool chaseLocked;
    bool pitFalling;
    float pitFallSpeed,pitFallDistance;
    float chaseBlend;
    bool crouchToggle;
    float mouseYawDelta;
    int fpsValue;
    int appliedResolution;
    float appliedLightDensity = -1f;
    double lastTime;
    double reportTime;
    double lastDebugTime;
    int reportFrames;
    float frameMs;
    bool appliedVsync;
    bool fullscreen;
    Rectangle windowedBounds;

    public GameWindow()
    {
        flicker=new LightFlicker(0);
        network = new SoloNetClient();
        menu.ConnectAction=ConnectOnline;
        menu.CancelJoinAction=CancelJoin;
        menu.SpawnAction=SpawnCubes;
        menu.ChatAction=text=>network is PhotonNetClient photon && photon.SendChat(text);
        menu.TitleAction=choice=>
        {
            if(choice==0) StartSoloSession();
            else if(choice==3) Close();
        };
        menu.SessionAction=choice=>
        {
            switch(choice)
            {
                case 0: menu.Toggle();if(!menu.Open) CloseSettings();break;
                case 1: LeaveSession();break;
                case 2: Close();break;
            }
        };
        menu.InteractionSound=()=>audio?.PlayClick();
        menu.HoverSound=()=>audio?.PlayHover();
        menu.InputSound=()=>audio?.PlayTerminalInput();
        menu.OutputSound=()=>audio?.PlayTerminalOutput();
        Text = "BEANROOMS";
        ClientSize = new Size(960, 720);
        string? localSide=Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(arg=>arg is "--local-left" or "--local-right");
        launchFullscreen=localSide==null;
        if(localSide!=null)
        {
            ClientSize=new Size(640,480);
            Rectangle area=DesktopBounds;
            int left=area.Left+Math.Max(0,(area.Width-1296)/2);
            int top=area.Top+Math.Max(0,(area.Height-520)/2);
            Location=new Point(left+(localSide=="--local-right"?656:0),top);
        }
        KeyDown += (_, e) =>
        {
            if(menu.Open)
            {
                if(e.KeyCode==Keys.Escape) ToggleSettings(back:true);
                else if(e.KeyCode==Keys.Enter)
                { menu.SubmitCommand();if(!menu.Open) CloseSettings(); }
                else if(e.KeyCode==Keys.Back) menu.BackspaceCommand();
                else if(e.KeyCode==Keys.PageUp)
                { if(menu.View==DebugMenu.MenuView.Network) menu.ScrollNetworkChat(5); else menu.ScrollTerminal(10); }
                else if(e.KeyCode==Keys.PageDown)
                { if(menu.View==DebugMenu.MenuView.Network) menu.ScrollNetworkChat(-5); else menu.ScrollTerminal(-10); }
                else if(e.Control && e.KeyCode==Keys.V && !string.IsNullOrEmpty(ClipboardText))
                    menu.PasteCommand(ClipboardText);
                else if(e.Control && e.KeyCode==Keys.A) menu.ClearCommand();
                else return;
                e.Handled=true;
                return;
            }
            keys.Add(e.KeyCode);
            if(e.KeyCode==Keys.Escape) ReleaseMouse();
        };
        KeyPress += (_, e) =>
        {
            if(!menu.Open) return;
            menu.TypeCommandCharacter(e.KeyChar);
            e.Handled=true;
        };
        KeyUp += (_, e) =>
        {
            keys.Remove(e.KeyCode);
            if(menu.Open || !HasPlayer) return;
            if(e.KeyCode==Keys.C) crouchToggle=!crouchToggle;
        };
        MouseDown += (_, e) =>
        {
            if(menu.Open)
            {
                if(e.Button==MouseButtons.Left && MenuPixel(e.Location) is Point point)
                    menu.ClickTerminal(point.X,point.Y);
                return;
            }
            if(!HasPlayer) return;
            wantsCapture=true; CaptureMouse();
            if(e.Button==MouseButtons.Right) rotatingHeld=true;
            if(e.Button!=MouseButtons.Left || !active) return;
            grabYaw=grabPitch=0f;
            if(network.Connected)
            {
                networkHolding=true;networkHeldId=0;
                SendPlayerState();
                network.Send(new NetMessage { Type="pickup",GrabSequence=++grabSequence });
            }
            else
            {
                ViewPose(out Vector3 eye,out Vector3 forward);
                physics.HoldDistance=ObjectInteractionProfile.HoldDistance;
                physics.TryPickup(eye,forward,ViewRotation(),boxes);
            }
        };
        MouseMove += (_, e) =>
        {
            if(menu.Open && MenuPixel(e.Location) is Point point)
                menu.DragTerminalSlider(point.X);
        };
        MouseWheel += (_, e) => { if(e.Delta==0) return; if(menu.Open) { if(menu.View==DebugMenu.MenuView.Network) menu.ScrollNetworkChat(Math.Sign(e.Delta)*3); else menu.ScrollTerminal(Math.Sign(e.Delta)*3); return; } if(!HasPlayer) return; int step=Math.Sign(e.Delta); if(network.Connected) { if(networkHolding || networkCubes.Any(cube=>cube.HolderId==network.Id && network.Id!=0)) network.Send(new NetMessage { Type="grab_scroll",Action=step }); } else physics.NudgeGrabDistance(0,step*0.2f); };
        MouseUp += (_, e) => { if(menu.Open) { if(e.Button==MouseButtons.Left) menu.ReleaseTerminalSlider();return; } if(!HasPlayer) return; if(e.Button==MouseButtons.Right) rotatingHeld=false; if(e.Button==MouseButtons.Left) { if(network.Connected) network.Send(new NetMessage { Type="release" }); else physics.Release(); networkHolding=false;networkHeldId=0;grabYaw=grabPitch=0f; } };
        Activated += (_, _) => { if(ready && wantsCapture) BeginInvoke(new Action(CaptureMouse)); };
        Deactivate += (_, _) => { networkHolding=false;rotatingHeld=false; network.Send(new NetMessage { Type="release" }); physics.Release(); SuspendMouse(); };
        FormClosed += (_, _) => { audio?.Dispose(); network.Dispose(); physics.Dispose(); menuBeans.Dispose(); ReleaseMouse(); };
    }

    Point? MenuPixel(Point pointer)
    {
        Size size=ClientSize;
        float contentWidth=Math.Min(size.Width,size.Height*4f/3f);
        float contentHeight=contentWidth*3f/4f;
        float left=(size.Width-contentWidth)/2f,top=(size.Height-contentHeight)/2f;
        if(pointer.X<left || pointer.X>=left+contentWidth || pointer.Y<top || pointer.Y>=top+contentHeight)
            return null;
        return new Point((int)((pointer.X-left)*320f/contentWidth),(int)((pointer.Y-top)*240f/contentHeight));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if(keyData == Keys.F11)
        {
            ToggleFullscreen();
            return true;
        }
        if(keyData == Keys.Tab && HasPlayer)
        {
            ToggleSettings();
            return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }

    void ToggleFullscreen()
    {
        SuspendMouse();
        if(fullscreen)
        {
            Fullscreen=false;
            Bounds=windowedBounds;
            fullscreen=false;
        }
        else
        {
            windowedBounds=Bounds;
            Fullscreen=true;
            fullscreen=true;
        }
        if(wantsCapture && !menu.Open) BeginInvoke(new Action(CaptureMouse));
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            Gl.Load(GetGlAddress);
            Gl.GenVertexArrays(1,out int vao); Gl.BindVertexArray(vao);
            pathProgram = Compile("screen.vert", "pathtrace.frag");
            probeProgram = Compile("screen.vert", "probe.frag");
            displayProgram = Compile("screen.vert", "display.frag");
            menuBackgroundProgram = Compile("screen.vert", "menu_background.frag");
            exposureProgram = Compile("screen.vert", "exposure.frag");
            bloomExtractProgram = Compile("screen.vert", "bloom_extract.frag");
            bloomBlurProgram = Compile("screen.vert", "bloom_blur.frag");
            framebuffer = Gl.GenFramebuffer();
            worldTexture = Gl.GenTexture();
            heightTexture = Gl.GenTexture();
            pitTexture = Gl.GenTexture();
            fixtureTexture = Gl.GenTexture();
            objectTexture = Gl.GenTexture();
            overlayTexture = Gl.GenTexture();
            Gl.BindTexture(0x0DE1,overlayTexture);
            Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
            Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
            wallTexture = LoadImage("wall.png");
            floorTexture = LoadImage("floor.png");
            ceilingTexture = LoadImage("ceiling.png");
            faceTexture = LoadImage("beanface.png");
            eyeTexture = LoadImage("eye.png");
            CreateRenderTarget();
            CreateProbeTargets();
            CreateExposureTargets();
            audio=new GameAudio();
            localMonster=null;
            visibleMonster=null;
            ready=true;
            menu.ShowTitle();
            wantsCapture=false;
            if(launchFullscreen) ToggleFullscreen();
            else if(!menu.Open) BeginInvoke(new Action(CaptureMouse));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"crash.log"),ex.ToString());
            Console.Error.WriteLine(ex);
            Close();
        }
    }

    protected override void OnFrame()
    {
        if(!ready) return;
        {
            if(menu.FpsLimit > 0 && lastTime > 0)
            {
                double nextFrame = lastTime + 1.0 / menu.FpsLimit;
                while(true)
                {
                    double remaining = nextFrame - watch.Elapsed.TotalSeconds;
                    if(remaining <= 0) break;
                    if(remaining>0.002) Thread.Sleep(Math.Max(1,(int)(remaining*1000)-1));
                    else Thread.SpinWait(32);
                }
            }
            double now=watch.Elapsed.TotalSeconds;
            float dt=(float)Math.Min(now-lastTime,0.05); lastTime=now;
            menu.AdvanceTerminal(dt);
            UpdateGame(dt);
            if(!HasPlayer)
            {
                menuBeans.Step(dt);
                audio?.UpdateMenu(dt);
                Render();
                return;
            }
            // Chase lighting is shared by everyone in the room, regardless of which player the monster sees.
            chaseLocked=menu.MonsterSettings.Enabled && visibleMonster?.Mode==2 &&
                visibleMonster.TargetId>0;
            float target=chaseLocked?1f:0f;
            float response=target>chaseBlend?FlickerProfile.FogRise:FlickerProfile.FogFall;
            chaseBlend+=(target-chaseBlend)*(1f-MathF.Exp(-response*dt));
            ViewPose(out Vector3 audioEye,out _);
            CubeSoundSource[] cubeSounds=network.Connected
                ? networkCubes.Select(cube=>new CubeSoundSource(cube.Id,new Vector3(cube.X,cube.Y,cube.Z),
                    new Vector3(cube.Vx,cube.Vy,cube.Vz),cube.HalfSize,cube.Grounded,cube.HolderId!=0,
                    new Quaternion(cube.Qx,cube.Qy,cube.Qz,cube.Qw),new Vector3(cube.Ax,cube.Ay,cube.Az),
                    cube.SlipSpeed,cube.Material)).ToArray()
                : physics.Bodies.Select(cube=>new CubeSoundSource(cube.Id,cube.Position,cube.Velocity,
                    cube.HalfSize,cube.Grounded,cube.HasHoldTarget,cube.Rotation,cube.AngularVelocity,
                    PhysicsBodies.ContactSlipSpeed(cube),cube.Material)).ToArray();
            CubeImpactEvent[] impacts=network.Connected?network.ConsumeCubeImpacts():physics.DrainCubeImpacts();
            audio?.Update(audioEye,smoothYaw+motion.YawOffset,activeFixtures,menu.Lights,
                visibleMonster,menu.MonsterSettings.Enabled,motion.Speed,motion.SprintBlend,motion.CrouchBlend,dt,
                remoteFootsteps,cubeSounds,impacts,boxes);
            Render();
        }
    }

}
