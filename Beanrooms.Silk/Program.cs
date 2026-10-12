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
    const int Grid = 48;
    const int FixtureGrid = 25;
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
    Vector3 camera = new(96, 1.68f, 96);
    float yaw = 0.63f, pitch;
    float smoothYaw, smoothPitch;
    bool lookInitialized;
    int seed = 82317;
    int width = 320, height = 240;
    int frames, worldTexture, fixtureTexture, objectTexture, wallTexture, floorTexture, ceilingTexture, faceTexture, eyeTexture, overlayTexture;
    int pathProgram, probeProgram, displayProgram, exposureProgram, bloomExtractProgram, bloomBlurProgram, framebuffer;
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
        flicker=new LightFlicker(0,menu.FlickerSettings);
        network = new SoloNetClient();
        menu.ConnectAction=ConnectOnline;
        menu.CancelJoinAction=CancelJoin;
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
                if(menu.EditingText)
                {
                    bool handled=true;
                    if(e.KeyCode is Keys.Escape or Keys.Enter) menu.FinishEditing();
                    else if(e.KeyCode==Keys.Back)
                    { if(menu.EditingName) menu.BackspaceName(); else menu.BackspaceConnection(); }
                    else if(e.KeyCode==Keys.Delete && !menu.EditingName) menu.ClearConnectionField();
                    else if(e.Control && e.KeyCode==Keys.A && !menu.EditingName) menu.ClearConnectionField();
                    else if(e.Control && e.KeyCode==Keys.V && !menu.EditingName && !string.IsNullOrEmpty(ClipboardText))
                        menu.PasteConnectionText(ClipboardText);
                    else handled=false;
                    e.Handled=handled;
                    return;
                }
                if(e.KeyCode==Keys.Escape) ToggleSettings(back:true);
                else if(e.KeyCode==Keys.Q) menu.CyclePage(-1);
                else if(e.KeyCode==Keys.E) menu.CyclePage(1);
                else if(e.KeyCode==Keys.Up) menu.Select(-1);
                else if(e.KeyCode==Keys.Down) menu.Select(1);
                else if(e.KeyCode==Keys.Left) menu.AdjustSelected(-1);
                else if(e.KeyCode is Keys.Right or Keys.Enter or Keys.Space) menu.AdjustSelected(1);
                e.Handled=true;
                return;
            }
            keys.Add(e.KeyCode);
            if(e.KeyCode==Keys.Escape) ReleaseMouse();
        };
        KeyPress += (_, e) =>
        {
            if(!menu.Open || !menu.EditingText) return;
            if(menu.EditingName) menu.EditNameCharacter(e.KeyChar);
            else menu.EditConnectionCharacter(e.KeyChar);
            e.Handled=true;
        };
        KeyUp += (_, e) =>
        {
            keys.Remove(e.KeyCode);
            if(menu.Open) return;
            if(e.KeyCode==Keys.C) crouchToggle=!crouchToggle;
        };
        MouseDown += (_, e) =>
        {
            if(menu.Open)
            {
                if(TryMenuPoint(e.Location,out Point point)) menu.MouseDown(point.X,point.Y);
                if(!menu.Open) CloseSettings();
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
                physics.HoldDistance=menu.HoldDistance;
                physics.TryPickup(eye,forward,ViewRotation(),boxes);
            }
        };
        MouseMove += (_, e) => { if(menu.Open && TryMenuPoint(e.Location,out Point point)) menu.MouseMove(point.X,point.Y); };
        MouseWheel += (_, e) => { if(e.Delta==0) return; if(menu.Open) { menu.MouseWheel(e.Delta); return; } if(!HasPlayer) return; int step=Math.Sign(e.Delta); if(network.Connected) { if(networkHolding || networkCubes.Any(cube=>cube.HolderId==network.Id && network.Id!=0)) network.Send(new NetMessage { Type="grab_scroll",Action=step }); } else physics.NudgeGrabDistance(0,step*0.2f); };
        MouseUp += (_, e) => { menu.MouseUp(); if(!HasPlayer) return; if(e.Button==MouseButtons.Right) rotatingHeld=false; if(e.Button==MouseButtons.Left) { if(network.Connected) network.Send(new NetMessage { Type="release" }); else physics.Release(); networkHolding=false;networkHeldId=0;grabYaw=grabPitch=0f; } };
        Activated += (_, _) => { if(ready && wantsCapture) BeginInvoke(new Action(CaptureMouse)); };
        Deactivate += (_, _) => { networkHolding=false;rotatingHeld=false; network.Send(new NetMessage { Type="release" }); physics.Release(); SuspendMouse(); };
        FormClosed += (_, _) => { audio?.Dispose(); network.Dispose(); physics.Dispose(); ReleaseMouse(); };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if(keyData == Keys.F11)
        {
            ToggleFullscreen();
            return true;
        }
        if(keyData == Keys.Tab)
        {
            ToggleSettings();
            return true;
        }
        if(menu.Open)
        {
            switch(keyData)
            {
                case Keys.Up: menu.Select(-1); return true;
                case Keys.Down: menu.Select(1); return true;
                case Keys.Left: menu.AdjustSelected(-1); return true;
                case Keys.Right: menu.AdjustSelected(1); return true;
            }
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }

    Rectangle PresentationArea()
    {
        int viewWidth=Math.Min(ClientSize.Width,ClientSize.Height*4/3);
        int viewHeight=Math.Min(ClientSize.Height,ClientSize.Width*3/4);
        return new Rectangle((ClientSize.Width-viewWidth)/2,(ClientSize.Height-viewHeight)/2,viewWidth,viewHeight);
    }

    bool TryMenuPoint(Point mouse,out Point menuPoint)
    {
        Rectangle area=PresentationArea();
        menuPoint=Point.Empty;
        if(area.Width<=0 || area.Height<=0 || !area.Contains(mouse)) return false;
        menuPoint=new Point((mouse.X-area.Left)*320/area.Width,(mouse.Y-area.Top)*240/area.Height);
        return true;
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
            exposureProgram = Compile("screen.vert", "exposure.frag");
            bloomExtractProgram = Compile("screen.vert", "bloom_extract.frag");
            bloomBlurProgram = Compile("screen.vert", "bloom_blur.frag");
            framebuffer = Gl.GenFramebuffer();
            worldTexture = Gl.GenTexture();
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
            GenerateWorld();
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

    void GenerateWorld()
    {
        probeDirty=true;
        physics.Clear();
        var layout=WorldLayout.Generate(seed);
        grid=layout.Grid;
        boxes.Clear();
        boxes.AddRange(layout.Walls);
        audio?.SetWorld(boxes);
        localMonster = new MonsterBrain(seed,boxes,menu.MonsterSettings);
        visibleMonster = localMonster.State;
        Gl.BindTexture(0x0DE1,worldTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        var handle = GCHandle.Alloc(grid,GCHandleType.Pinned);
        try { Gl.TexImage2D(0x0DE1,0,0x8814,Grid,Grid,0,0x1908,0x1406,handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
        RebuildFixtureMap();
        Vector2 clearSpawn=WorldLayout.FindClearSpawn(boxes,new Vector2(96f,96f),Radius);
        camera = new Vector3(clearSpawn.X,1.68f,clearSpawn.Y);
        smoothYaw = yaw;
        smoothPitch = pitch;
        lookInitialized = true;
        menu.BodyCount=physics.Bodies.Count; menu.Dirty=true;
    }
    static float FixtureHash(int x,int z,int worldSeed)
    {
        unchecked
        {
            uint value=(uint)x*1597334677u ^ (uint)z*3812015801u ^ (uint)worldSeed*95828261u;
            value ^= value >> 16; value *= 2246822519u;
            value ^= value >> 13; value *= 3266489917u;
            value ^= value >> 16;
            return (value >> 8)*(1f/16777216f);
        }
    }
    bool FixtureClear(int x,int z)
    {
        float cx=x*8f, cz=z*8f, minX=cx-1f, minZ=cz-1f, maxX=cx+1f, maxZ=cz+1f;
        int x0=Math.Clamp((int)MathF.Floor(minX/Cell),0,Grid-1);
        int z0=Math.Clamp((int)MathF.Floor(minZ/Cell),0,Grid-1);
        int x1=Math.Clamp((int)MathF.Floor(maxX/Cell),0,Grid-1);
        int z1=Math.Clamp((int)MathF.Floor(maxZ/Cell),0,Grid-1);
        for(int zz=z0;zz<=z1;zz++) for(int xx=x0;xx<=x1;xx++)
        {
            int offset=(zz*Grid+xx)*4;
            if(grid[offset]>=0f && maxX>grid[offset] && minX<grid[offset+2] &&
                maxZ>grid[offset+1] && minZ<grid[offset+3]) return false;
        }
        return true;
    }
    void RebuildFixtureMap()
    {
        probeDirty=true;
        Array.Clear(fixturePixels);
        activeFixtures.Clear();
        for(int z=0;z<FixtureGrid;z++) for(int x=0;x<FixtureGrid;x++)
        {
            bool enabled=(x==12 && z==12) ||
                (FixtureHash(x/3+47,z/3-29,seed)>=0.26f &&
                 FixtureHash(x,z,seed)>1f-menu.LightDensity);
            int offset=(z*FixtureGrid+x)*4;
            bool active=enabled && FixtureClear(x,z);
            fixtureBase[z*FixtureGrid+x]=active;
            fixturePixels[offset]=active ? 1f : 0f;
            fixturePixels[offset+2]=active ? 1f : 0f;
            if(active) activeFixtures.Add(new Vector2(x*8f,z*8f));
            fixturePixels[offset+1]=FixtureHash(x+91,z-37,seed)<0.08f ? 1f : 0f;
        }
        Bind(4,fixtureTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        var handle=GCHandle.Alloc(fixturePixels,GCHandleType.Pinned);
        try { Gl.TexImage2D(0x0DE1,0,0x8814,FixtureGrid,FixtureGrid,0,0x1908,0x1406,handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
        appliedLightDensity=menu.LightDensity;
    }

    void UpdateFixtureFlicker()
    {
        double now=watch.Elapsed.TotalSeconds;
        flicker.UpdateRandom(now,network.Connected);
        flicker.UpdateChase(now,chaseLocked);
        float redChance=0.08f;
        if(menu.FlickerSettings.ChaseRed)
            redChance+=(menu.FlickerSettings.ChaseRedChance-0.08f)*chaseBlend;
        bool changed=false;
        for(int z=0;z<FixtureGrid;z++) for(int x=0;x<FixtureGrid;x++)
        {
            int index=z*FixtureGrid+x;
            float on=fixtureBase[index] && flicker.IsOn(x,z,seed,now) ? 1f : 0f;
            if(fixturePixels[index*4]!=on) { fixturePixels[index*4]=on;changed=true; }
            float red=FixtureHash(x+91,z-37,seed)<redChance ? 1f : 0f;
            if(fixturePixels[index*4+1]!=red) { fixturePixels[index*4+1]=red;changed=true; }
        }
        if(!changed) return;
        probeDirty=true;
        activeFixtures.Clear();
        for(int z=0;z<FixtureGrid;z++) for(int x=0;x<FixtureGrid;x++)
            if(fixturePixels[(z*FixtureGrid+x)*4]>0.5f)
                activeFixtures.Add(new Vector2(x*8f,z*8f));
        Bind(4,fixtureTexture);
        var handle=GCHandle.Alloc(fixturePixels,GCHandleType.Pinned);
        try { Gl.TexImage2D(0x0DE1,0,0x8814,FixtureGrid,FixtureGrid,0,0x1908,0x1406,handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }
    void Regenerate()
    {
        seed = Random.Shared.Next(1,1_000_000_000);
        GenerateWorld();
        motion.Reset();
        BeginSpawn(false);
    }

    void StartSoloSession()
    {
        DisconnectNetwork();
        sessionPhase=GameSessionPhase.PlayingSolo;
        Regenerate();
        menu.LeaveTitle();
        CloseSettings();
    }

    void LeaveSession()
    {
        if(network.Connected) network.Send(new NetMessage { Type="release" });
        physics.Release();
        physics.Clear();
        DisconnectNetwork();
        sessionPhase=GameSessionPhase.MainMenu;
        localMonster=null;
        visibleMonster=null;
        networkCubes=[];
        remotePositions.Clear();remoteYaws.Clear();remoteColors.Clear();remoteNames.Clear();
        remoteFootsteps.Clear();
        motion.Reset();
        crouchToggle=false;rotatingHeld=false;networkHolding=false;networkHeldId=0;
        chaseLocked=false;chaseBlend=0f;mouseYawDelta=0f;
        menu.BodyCount=0;
        menu.ShowTitle();
        wantsCapture=false;
        SuspendMouse();
    }

    void BeginSpawn(bool monsterHit)
    {
        double now=watch.Elapsed.TotalSeconds;
        spawnTransition.Begin(now,monsterHit);
        if(monsterHit || now-lastSpawnSound>0.5)
        {
            audio?.PlaySpawn();
            lastSpawnSound=now;
        }
    }

    void RespawnLocal()
    {
        if(visibleMonster==null) return;
        Vector2 point=WorldLayout.FindSafeRespawn(boxes,
            new Vector2(visibleMonster.X,visibleMonster.Z),Radius,Random.Shared);
        physics.Release();
        camera=new Vector3(point.X,1.68f,point.Y);
        motion.Reset();
        crouchToggle=false;
        localRespawnProtectedUntil=watch.Elapsed.TotalSeconds+2.5;
        BeginSpawn(true);
    }

    void CaptureMouse() { if (ready && wantsCapture && !active && Focused) { active=true; CaptureCursor(); } }
    void SuspendMouse() { if (active) { active=false; ReleaseCursor(); } keys.Clear(); }
    void ReleaseMouse() { wantsCapture=false; SuspendMouse(); }
    void ToggleSettings(bool back=false)
    {
        bool opening=!menu.Open;
        networkHolding=false;

        network.Send(new NetMessage { Type="release" });
        physics.Release();
        if(back) menu.Back(); else menu.Toggle();
        if(opening && !menu.TitleScreen) menu.SetPage(9);
        if(menu.Open) { wantsCapture=false; SuspendMouse(); }
        else CloseSettings();
    }
    void CloseSettings()
    {
        wantsCapture=HasPlayer;
        if(wantsCapture) BeginInvoke(new Action(CaptureMouse));
    }
    void ConnectOnline()
    {
        if(HasPlayer) return;
        menu.FinishEditing();
        if(string.IsNullOrWhiteSpace(menu.JoinKey))
        {
            menu.NetworkStatus="ENTER KEY"; menu.Dirty=true;
            return;
        }
        DisconnectNetwork();
        network=new PhotonNetClient(menu.JoinKey);
        networkStarted=true;
        sessionPhase=GameSessionPhase.JoiningRoom;
        menu.NetworkStatus="CONNECTING";
        menu.Dirty=true;
        network.Start();
    }
    void CancelJoin()
    {
        if(sessionPhase!=GameSessionPhase.JoiningRoom) return;
        DisconnectNetwork();
        sessionPhase=GameSessionPhase.MainMenu;
    }
    void DisconnectNetwork()
    {
        network.Dispose();
        network=new SoloNetClient();
        networkStarted=false;
        networkHolding=false;

        menu.NetworkConnected=false;
        menu.NetworkId=0;
        menu.NetworkHost=0;
        menu.NetworkPlayers=1;
        menu.NetworkStatus="OFFLINE";
        menu.Dirty=true;
    }
    protected override void OnRawMouse(float dx,float dy)
    {
        if(active && Focused) MoveMouse((int)MathF.Round(dx),(int)MathF.Round(dy));
    }
    void MoveMouse(int dx,int dy)
    {
        if(dx==0 && dy==0) return;
        dx=Math.Clamp(dx,-100,100); dy=Math.Clamp(dy,-100,100);
        if(rotatingHeld && (physics.Held!=null || networkHolding || networkCubes.Any(cube=>cube.HolderId==network.Id && network.Id!=0)))
        {
            grabYaw=MathF.IEEERemainder(grabYaw+dx*menu.MouseSensitivity,MathF.Tau);
            grabPitch=Math.Clamp(grabPitch-dy*menu.MouseSensitivity,-1.5f,1.5f);
            return;
        }
        float yawChange=dx*menu.MouseSensitivity;
        yaw += yawChange;
        mouseYawDelta += yawChange;
        pitch=Math.Clamp(pitch-dy*menu.MouseSensitivity,-1.35f,1.35f);
    }

    static Vector3 CubeLocalExtents(CubeState cube)
        => cube.Plank ? new Vector3(.90f,.11f,.90f) : new Vector3(cube.HalfSize);

    float StepSupport(float x,float z)
    {
        float height=0f;
        if(network.Connected)
        {
            foreach(CubeState cube in networkCubes)
            {
                if(!cube.Plank || cube.HolderId!=0) continue;
                Quaternion rotation=new(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
                if(rotation.LengthSquared()<.001f) rotation=Quaternion.Identity;
                Vector3 extent=PhysicsBodies.RotatedExtents(CubeLocalExtents(cube),rotation);
                if(MathF.Abs(x-cube.X)<extent.X+Radius && MathF.Abs(z-cube.Z)<extent.Z+Radius)
                    height=MathF.Max(height,cube.Y+extent.Y);
            }
        }
        else foreach(RigidCube body in physics.Bodies)
        {
            if(!body.Plank || body.HasHoldTarget) continue;
            Vector3 extent=PhysicsBodies.Extents(body);
            if(MathF.Abs(x-body.Position.X)<extent.X+Radius && MathF.Abs(z-body.Position.Z)<extent.Z+Radius)
                height=MathF.Max(height,body.Position.Y+extent.Y);
        }
        return height;
    }

    bool Collides(float x,float z,float footHeight)
    {
        if(x<4+Radius||z<4+Radius||x>Grid*Cell-4-Radius||z>Grid*Cell-4-Radius) return true;
        foreach(var b in boxes) if(x>b.Left-Radius&&x<b.Right+Radius&&z>b.Top-Radius&&z<b.Bottom+Radius) return true;
        if(network.Connected)
        {
            foreach(CubeState cube in networkCubes)
            {
                Quaternion rotation=new(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
                if(rotation.LengthSquared()<.001f) rotation=Quaternion.Identity;
                Vector3 extent=PhysicsBodies.RotatedExtents(CubeLocalExtents(cube),rotation);
                if(MathF.Abs(x-cube.X)<extent.X+Radius && MathF.Abs(z-cube.Z)<extent.Z+Radius &&
                   cube.Y+extent.Y>footHeight+.025f && cube.Y-extent.Y<footHeight+2.1f) return true;
            }
        }
        else if(physics.CollidesPlayer(x,z,Radius,footHeight)) return true;
        return false;
    }

    void ViewPose(out Vector3 eye,out Vector3 forward)
    {
        eye=camera+motion.ViewOffset;
        float viewYaw=smoothYaw+motion.YawOffset;
        float viewPitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f);
        forward=CameraOrientation.Forward(viewYaw,viewPitch);
    }
    Quaternion ViewRotation()
    {
        float viewYaw=smoothYaw+motion.YawOffset;
        float viewPitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f);
        return CameraOrientation.FromAngles(viewYaw,viewPitch,motion.Roll);
    }
    void SendPlayerState()
    {
        if(!network.Connected) return;
        ViewPose(out Vector3 eye,out _);
        network.Send(new NetMessage { Type="state",X=eye.X,Y=eye.Y,Z=eye.Z,
            MoveSpeed=motion.Speed,SprintBlend=motion.SprintBlend,CrouchBlend=motion.CrouchBlend,
            Yaw=smoothYaw+motion.YawOffset,Pitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f),
            HoldDistance=menu.HoldDistance,PickupRange=menu.PickupRange,GrabYaw=grabYaw,GrabPitch=grabPitch,
            SpringFrequency=menu.SpringFrequency,SpringDamping=menu.SpringDamping,
            PidSmoothing=menu.PidSmoothing,SlideResistance=menu.SlideResistance,SnapHold=menu.SnapHold,SnapStep=menu.SnapStep,
            MonsterTuning=menu.MonsterSettings.Clone(),FlickerTuning=menu.FlickerSettings.Clone(),
            Name=menu.PlayerName,RespawnId=respawnId,
            R=menu.PlayerRed,G=menu.PlayerGreen,B=menu.PlayerBlue });
    }

    void UpdateGame(float dt)
    {
        network.Service(dt);
        if(sessionPhase==GameSessionPhase.PlayingOnline && !network.Connected)
        {
            LeaveSession();
            return;
        }
        bool movementWritable=!network.Connected;
        if(menu.MovementControlsWritable!=movementWritable)
        {
            if(!movementWritable) menu.ResetMovementDefaults();
            menu.MovementControlsWritable=movementWritable;
            if(!movementWritable && menu.Page==2 && menu.Open && !menu.TitleScreen) menu.SetPage(9);
            menu.Dirty=true;
        }
        string connectionStatus=network.Status;
        if(networkStarted && menu.NetworkStatus!=connectionStatus)
        { menu.NetworkStatus=connectionStatus;menu.Dirty=true; }
        NetMessage? lightEvent=network.ConsumeFlicker();
        if(lightEvent!=null) flicker.Trigger(watch.Elapsed.TotalSeconds,lightEvent.BlackoutAfter);
        NetMessage? world=network.ConsumeWorld();
        if(world!=null)
        {
            flicker.Restore(watch.Elapsed.TotalSeconds,world.Blackout,world.BlackoutAfter);
            if(world.FlickerAge>=0f && world.FlickerAge<menu.FlickerSettings.EventDuration)
                flicker.Trigger(watch.Elapsed.TotalSeconds-world.FlickerAge,world.BlackoutAfter);
            if(seed!=world.Seed) { seed=world.Seed; GenerateWorld(); }
            camera=new Vector3(world.X,world.Y,world.Z);
            motion.Reset();
            networkCubes=[];

            visibleMonster=null;
            remotePositions.Clear();
            remoteYaws.Clear();
            remoteColors.Clear();
            remoteNames.Clear();
            menu.BodyCount=0; menu.Dirty=true;
            respawnId=world.RespawnId;
            nextNetworkSend=0;
            BeginSpawn(false);
            if(sessionPhase==GameSessionPhase.JoiningRoom)
            {
                sessionPhase=GameSessionPhase.PlayingOnline;
                menu.LeaveTitle();
                CloseSettings();
            }
        }
        if(!HasPlayer) { mouseYawDelta=0f;return; }
        NetMessage? respawn=network.ConsumeRespawn();
        if(respawn!=null)
        {
            respawnId=respawn.RespawnId;
            camera=new Vector3(respawn.X,respawn.Y,respawn.Z);
            motion.Reset();
            crouchToggle=false;
            networkHolding=false;

            physics.Release();
            nextNetworkSend=0;
            BeginSpawn(true);
        }
        Vector2 input=Vector2.Zero;
        if(active)
        {
            if(keys.Contains(Keys.W)||keys.Contains(Keys.Up)) input.Y+=1f;
            if(keys.Contains(Keys.S)||keys.Contains(Keys.Down)) input.Y-=1f;
            if(keys.Contains(Keys.D)||keys.Contains(Keys.Right)) input.X+=1f;
            if(keys.Contains(Keys.A)||keys.Contains(Keys.Left)) input.X-=1f;
        }
        bool crouching=crouchToggle || keys.Contains(Keys.ControlKey) || keys.Contains(Keys.LControlKey) || keys.Contains(Keys.RControlKey);
        bool sprinting=keys.Contains(Keys.ShiftKey) || keys.Contains(Keys.LShiftKey) || keys.Contains(Keys.RShiftKey);
        motion.Update(dt,input,sprinting,crouching,mouseYawDelta,yaw,menu,ref camera,Collides,StepSupport,active);
        mouseYawDelta=0f;
        if(!lookInitialized) { smoothYaw=yaw; smoothPitch=pitch; lookInitialized=true; }
        float lookBlend=1f-MathF.Exp(-MathF.Max(menu.CameraSmooth,0.1f)*dt);
        smoothYaw += MathF.Atan2(MathF.Sin(yaw-smoothYaw),MathF.Cos(yaw-smoothYaw))*lookBlend;
        smoothPitch += (pitch-smoothPitch)*lookBlend;
        ViewPose(out Vector3 eye,out Vector3 forward);
        if(network.Connected)
        {
            double now=watch.Elapsed.TotalSeconds;
            if(now>=nextNetworkSend) { SendPlayerState(); nextNetworkSend=now+0.05; }
            var snapshot=network.Snapshot();
            int hostId=snapshot.HostId;
            if(hostId==0 && snapshot.Players.Length>0)
                hostId=snapshot.Players.Min(player=>player.Id);
            int count=snapshot.Players.Length;
            if(!menu.NetworkConnected || menu.NetworkId!=network.Id || menu.NetworkHost!=hostId || menu.NetworkPlayers!=count)
            { menu.NetworkConnected=true;menu.NetworkId=network.Id;menu.NetworkHost=hostId;menu.NetworkPlayers=count;menu.Dirty=true; }
            bool writable=hostId==network.Id;
            if(menu.MonsterControlsWritable!=writable) { menu.MonsterControlsWritable=writable;menu.Dirty=true; }
            if(!writable&&snapshot.MonsterTuning!=null&&menu.MonsterSettings.CopyFrom(snapshot.MonsterTuning))
                menu.Dirty=true;
            if(menu.FlickerControlsWritable!=writable) { menu.FlickerControlsWritable=writable;menu.Dirty=true; }
            if(!writable&&snapshot.FlickerTuning!=null&&menu.FlickerSettings.CopyFrom(snapshot.FlickerTuning))
                menu.Dirty=true;
            if(snapshot.Monster!=null)
            {
                MonsterState target=snapshot.Monster;
                if(visibleMonster==null) visibleMonster=new MonsterState
                    { X=target.X,Y=target.Y,Z=target.Z,Yaw=target.Yaw,Mode=target.Mode };
                float monsterBlend=1f-MathF.Exp(-15f*dt);
                visibleMonster.X+=(target.X-visibleMonster.X)*monsterBlend;
                visibleMonster.Y+=(target.Y-visibleMonster.Y)*monsterBlend;
                visibleMonster.Z+=(target.Z-visibleMonster.Z)*monsterBlend;
                visibleMonster.Yaw+=MathF.Atan2(MathF.Sin(target.Yaw-visibleMonster.Yaw),MathF.Cos(target.Yaw-visibleMonster.Yaw))*monsterBlend;
                visibleMonster.Mode=target.Mode;
                visibleMonster.TargetId=target.TargetId;
                visibleMonster.GoalX=target.GoalX;visibleMonster.GoalZ=target.GoalZ;
                visibleMonster.HasLastSeen=target.HasLastSeen;
                visibleMonster.LastX=target.LastX;visibleMonster.LastZ=target.LastZ;
                visibleMonster.Route=target.Route;
            }
            CubeState[] cubes=snapshot.Cubes;
            NetMessage? pickupResult=network.ConsumePickupResult();
            if(pickupResult!=null && pickupResult.GrabSequence==grabSequence)
            {
                networkHeldId=networkHolding?pickupResult.Action:0;
                if(pickupResult.Action==0) networkHolding=false;
            }
            if(networkCubes.Length!=cubes.Length || cubes.Where((cube,i)=>cube.Id!=networkCubes[i].Id).Any())
                networkCubes=cubes.Select(CloneCube).ToArray();
            else for(int i=0;i<cubes.Length;i++)
            {
                CubeState target=cubes[i], current=networkCubes[i];
                if(current.Id!=target.Id) { networkCubes[i]=CloneCube(target);continue; }
                float blend=1f-MathF.Exp(-24f*dt);
                current.X+= (target.X-current.X)*blend;
                current.Y+= (target.Y-current.Y)*blend;
                current.Z+= (target.Z-current.Z)*blend;
                Quaternion qa=new(current.Qx,current.Qy,current.Qz,current.Qw);
                Quaternion qb=new(target.Qx,target.Qy,target.Qz,target.Qw);
                Quaternion q=Quaternion.Slerp(qa,qb,blend);
                current.Qx=q.X;current.Qy=q.Y;current.Qz=q.Z;current.Qw=q.W;
                current.HalfSize=target.HalfSize;current.Emissive=target.Emissive;current.Plank=target.Plank;current.Material=target.Material;
                current.HolderId=target.HolderId;
                current.Vx=target.Vx;current.Vy=target.Vy;current.Vz=target.Vz;current.Grounded=target.Grounded;
                current.Ax=target.Ax;current.Ay=target.Ay;current.Az=target.Az;
                current.SlipSpeed=target.SlipSpeed;
                current.R=target.R;current.G=target.G;current.B=target.B;
            }
            var seen=new HashSet<int>();
            remoteFootsteps.Clear();
            foreach(PlayerState player in snapshot.Players)
            {
                if(player.Id==network.Id) continue;
                seen.Add(player.Id);
                Vector3 target=new(player.X,player.Y,player.Z);
                remotePositions[player.Id]=remotePositions.TryGetValue(player.Id,out Vector3 current)
                    ? Vector3.Lerp(current,target,1f-MathF.Exp(-16f*dt)) : target;
                float targetYaw=player.Yaw;
                remoteYaws[player.Id]=remoteYaws.TryGetValue(player.Id,out float previousYaw)
                    ? previousYaw+MathF.Atan2(MathF.Sin(targetYaw-previousYaw),MathF.Cos(targetYaw-previousYaw))*(1f-MathF.Exp(-16f*dt))
                    : targetYaw;
                remoteColors[player.Id]=new Vector3(player.R,player.G,player.B);
                remoteNames[player.Id]=player.Name;
                remoteFootsteps.Add(new FootstepSource(player.Id,remotePositions[player.Id],player.MoveSpeed,
                    player.SprintBlend,player.CrouchBlend));
            }
            foreach(int id in remotePositions.Keys.Where(id=>!seen.Contains(id)).ToArray())
            { remotePositions.Remove(id); remoteYaws.Remove(id);remoteColors.Remove(id);remoteNames.Remove(id); }
            if(menu.BodyCount!=networkCubes.Length) { menu.BodyCount=networkCubes.Length; menu.Dirty=true; }
            return;
        }
        networkCubes=[];
        remotePositions.Clear();
        remoteYaws.Clear();
        remoteColors.Clear();
        remoteNames.Clear();
        remoteFootsteps.Clear();
        if(menu.NetworkConnected) { menu.NetworkConnected=false;menu.NetworkId=0;menu.NetworkHost=0;menu.NetworkPlayers=1;menu.Dirty=true; }
        networkHolding=false;

        if(!menu.MonsterControlsWritable) { menu.MonsterControlsWritable=true;menu.Dirty=true; }
        if(!menu.FlickerControlsWritable) { menu.FlickerControlsWritable=true;menu.Dirty=true; }
        physics.PickupRange=menu.PickupRange; physics.HoldDistance=menu.HoldDistance;
        physics.RotateYaw=grabYaw;physics.RotatePitch=grabPitch;
        physics.Frequency=menu.SpringFrequency; physics.Damping=menu.SpringDamping; physics.TargetSmoothing=menu.PidSmoothing;
        physics.SlideResistance=menu.SlideResistance;physics.SnapHold=menu.SnapHold;physics.SnapStep=menu.SnapStep;
        physics.Update(dt,eye,forward,ViewRotation(),boxes,camera);
        localMonster?.Update(dt,[new PlayerState { Id=1,X=eye.X,Y=eye.Y,Z=eye.Z,Yaw=smoothYaw }],physics.Bodies);
        visibleMonster=localMonster?.State;
        if(menu.MonsterSettings.Enabled && visibleMonster!=null &&
           watch.Elapsed.TotalSeconds>=localRespawnProtectedUntil &&
           Vector2.DistanceSquared(new Vector2(camera.X,camera.Z),new Vector2(visibleMonster.X,visibleMonster.Z))<=0.81f)
        {
            RespawnLocal();
            ViewPose(out eye,out forward);
        }
    }

    static CubeState CloneCube(CubeState source) => new()
    {
        Id=source.Id,HolderId=source.HolderId,
        X=source.X,Y=source.Y,Z=source.Z,Qx=source.Qx,Qy=source.Qy,Qz=source.Qz,Qw=source.Qw,
        Vx=source.Vx,Vy=source.Vy,Vz=source.Vz,Grounded=source.Grounded,
        Ax=source.Ax,Ay=source.Ay,Az=source.Az,SlipSpeed=source.SlipSpeed,
        HalfSize=source.HalfSize,Emissive=source.Emissive,Plank=source.Plank,Material=source.Material,R=source.R,G=source.G,B=source.B
    };
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
            UpdateGame(dt);
            // Chase lighting is shared by everyone in the room, regardless of which player the monster sees.
            chaseLocked=menu.MonsterSettings.Enabled && visibleMonster?.Mode==2 &&
                visibleMonster.TargetId>0;
            float target=chaseLocked?1f:0f;
            float response=target>chaseBlend?menu.FlickerSettings.FogRise:menu.FlickerSettings.FogFall;
            chaseBlend+=(target-chaseBlend)*(1f-MathF.Exp(-response*dt));
            ViewPose(out Vector3 audioEye,out _);
            CubeSoundSource[] cubeSounds=network.Connected
                ? networkCubes.Select(cube=>new CubeSoundSource(cube.Id,new Vector3(cube.X,cube.Y,cube.Z),
                    new Vector3(cube.Vx,cube.Vy,cube.Vz),cube.HalfSize,cube.Grounded,cube.HolderId!=0,
                    new Quaternion(cube.Qx,cube.Qy,cube.Qz,cube.Qw),new Vector3(cube.Ax,cube.Ay,cube.Az),
                    cube.SlipSpeed,cube.Material,CubeLocalExtents(cube))).ToArray()
                : physics.Bodies.Select(cube=>new CubeSoundSource(cube.Id,cube.Position,cube.Velocity,
                    cube.HalfSize,cube.Grounded,cube.HasHoldTarget,cube.Rotation,cube.AngularVelocity,
                    PhysicsBodies.ContactSlipSpeed(cube),cube.Material,PhysicsBodies.LocalExtents(cube))).ToArray();
            CubeImpactEvent[] impacts=network.Connected?network.ConsumeCubeImpacts():physics.DrainCubeImpacts();
            audio?.Update(audioEye,smoothYaw+motion.YawOffset,activeFixtures,menu.Lights,
                visibleMonster,menu.MonsterSettings.Enabled,motion.Speed,motion.SprintBlend,motion.CrouchBlend,dt,
                remoteFootsteps,cubeSounds,impacts,boxes);
            Render();
        }
    }

}
