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

    static bool TryServerEndpoint(string[] args,out string host,out int port)
    {
        host=""; port=27015;
        int index=Array.FindIndex(args,arg=>arg.Equals("--server",StringComparison.OrdinalIgnoreCase));
        if(index<0) return true;
        if(index+1>=args.Length) return false;
        string endpoint=args[index+1].Trim();
        string? joinKey=Environment.GetEnvironmentVariable("BEANROOMS_JOIN_KEY");
        if(!string.IsNullOrWhiteSpace(joinKey))
        {
            if(!TryOnlineEndpoint(endpoint,joinKey,out host)) return false;
            port=new Uri(host).Port;
            return true;
        }
        if(Uri.TryCreate(endpoint,UriKind.Absolute,out Uri? webSocketUri) &&
           webSocketUri.Scheme is "ws" or "wss")
        {
            if(webSocketUri.HostNameType==UriHostNameType.Unknown || webSocketUri.AbsolutePath!="/ws" ||
               webSocketUri.Fragment.Length>0 || webSocketUri.UserInfo.Length>0) return false;
            host=webSocketUri.AbsoluteUri;
            port=webSocketUri.Port;
            return true;
        }
        if(!Uri.TryCreate("tcp://"+endpoint,UriKind.Absolute,out Uri? uri) ||
           uri.HostNameType==UriHostNameType.Unknown || uri.AbsolutePath!="/" ||
           uri.Query.Length>0 || uri.Fragment.Length>0 || uri.UserInfo.Length>0)
            return false;
        int chosenPort=uri.IsDefaultPort ? 27015 : uri.Port;
        if(chosenPort<1 || chosenPort>65535) return false;
        host=uri.Host; port=chosenPort;
        return true;
    }

    internal static bool TryOnlineEndpoint(string address,string joinKey,out string endpoint)
    {
        endpoint="";
        if(string.IsNullOrWhiteSpace(address)||string.IsNullOrWhiteSpace(joinKey)) return false;
        string input=address.Trim();
        if(!input.Contains("://",StringComparison.Ordinal)) input="https://"+input;
        if(!Uri.TryCreate(input,UriKind.Absolute,out Uri? uri) ||
           uri.Scheme is not ("http" or "https" or "ws" or "wss") ||
           uri.HostNameType==UriHostNameType.Unknown ||
           uri.AbsolutePath is not ("/" or "/ws") ||
           uri.Query.Length>0 || uri.Fragment.Length>0 || uri.UserInfo.Length>0) return false;
        string scheme=uri.Scheme is "https" or "wss" ? "wss" : "ws";
        endpoint=$"{scheme}://{uri.Authority}/ws?key={Uri.EscapeDataString(joinKey.Trim())}";
        return true;
    }
}

internal sealed class GameWindow : SilkForm
{
    const int Grid = 48;
    const int FixtureGrid = 25;
    const float Cell = 4f;
    const float Radius = 0.4f;
    readonly Stopwatch watch = Stopwatch.StartNew();
    readonly HashSet<Keys> keys = new();
    readonly List<RectangleF> boxes = new();
    readonly SandboxWorld localSandbox = new();
    SandboxDevice[] networkDevices=[];
    SandboxWire[] networkWires=[];
    int pendingWireId;
    int pendingWeldId;
    SandboxKind? copiedDeviceKind;
    bool copiedCube,copiedEmissive;
    PhysicsMaterial copiedMaterial;
    Vector3 copiedColor;
    bool sandboxToggleKeyDown;
    bool wireToggleKeyDown,weldToggleKeyDown;
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
    bool remoteServer;
    bool networkStarted;
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

    public GameWindow(string serverHost = "", int serverPort = 27015)
    {
        flicker=new LightFlicker(0,menu.FlickerSettings);
        network = new LocalNetClient(serverHost.Length==0?"127.0.0.1":serverHost,serverPort);
        networkStarted=serverHost.Length>0;
        remoteServer = networkStarted && serverHost is not ("127.0.0.1" or "localhost" or "::1");
        menu.NetworkRemote = remoteServer;
        menu.ConnectAction=ConnectOnline;
        menu.DisconnectAction=DisconnectNetwork;
        menu.TitleAction=choice=>
        {
            if(choice==0) { DisconnectNetwork();Regenerate();menu.LeaveTitle();CloseSettings(); }
            else if(choice==3) Close();
        };
        menu.SessionAction=choice=>
        {
            switch(choice)
            {
                case 0: menu.Toggle();if(!menu.Open) CloseSettings();break;
                case 1: if(networkStarted) DisconnectNetwork();break;
                case 2: DisconnectNetwork();menu.ShowTitle();wantsCapture=false;SuspendMouse();break;
                case 3: Close();break;
            }
        };
        menu.InteractionSound=()=>audio?.PlayClick();
        menu.HoverSound=()=>audio?.PlayHover();
        Text = "BEANROOMS  |  WASD move  •  F cube  •  Y copy  •  V place  •  O ponder  •  T light event  •  Tab menu  •  F11 full screen";
        menu.ObjectAction = HandleObjectAction;
        menu.SandboxSpawn = SpawnSandboxItem;
        menu.SandboxToggle = ToggleSandboxPermission;
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
        KeyDown += (_, e) => {
            if(!menu.Open && e.KeyCode==Keys.O)
            {
                TogglePonderScreen();e.Handled=true;return;
            }
            if(menu.PonderPage)
            {
                if(e.KeyCode is Keys.Escape or Keys.O) TogglePonderScreen();
                else if(e.KeyCode is Keys.Left or Keys.Up) menu.MovePonder(-1);
                else if(e.KeyCode is Keys.Right or Keys.Down or Keys.Enter or Keys.Space) menu.MovePonder(1);
                e.Handled=true;return;
            }
            if(!menu.Open && e.KeyCode==Keys.B)
            {
                if(!sandboxToggleKeyDown) { sandboxToggleKeyDown=true;ToggleSandboxMenu(); }
                e.Handled=true;
                return;
            }
            if(!menu.Open && e.KeyCode==Keys.G)
            {
                if(!wireToggleKeyDown) { wireToggleKeyDown=true;menu.WireTool=!menu.WireTool;menu.WeldTool=false;pendingWireId=0;menu.Dirty=true; }
                e.Handled=true;return;
            }
            if(!menu.Open && e.KeyCode==Keys.K)
            {
                if(!weldToggleKeyDown) { weldToggleKeyDown=true;menu.WeldTool=!menu.WeldTool;menu.WireTool=false;pendingWeldId=0;menu.Dirty=true; }
                e.Handled=true;return;
            }
            if(menu.Open) {
                if(menu.SandboxPage)
                {
                    if(e.KeyCode is Keys.B or Keys.Escape) ToggleSandboxMenu();
                    menu.Dirty=true;e.Handled=true;return;
                }
                if(menu.EditingText)
                {
                    bool handled=true;
                    if(e.KeyCode is Keys.Escape or Keys.Enter) menu.FinishEditing();
                    else if(e.KeyCode == Keys.Back)
                    {
                        if(menu.EditingName) menu.BackspaceName(); else menu.BackspaceConnection();
                    }
                    else if(e.KeyCode == Keys.Delete && !menu.EditingName) menu.ClearConnectionField();
                    else if(e.Control && e.KeyCode==Keys.A && !menu.EditingName) menu.ClearConnectionField();
                    else if(e.Control && e.KeyCode==Keys.V && !menu.EditingName)
                    {
                        if(!string.IsNullOrEmpty(ClipboardText)) menu.PasteConnectionText(ClipboardText);
                    }
                    else handled=false;
                    e.Handled=handled;
                    return;
                }
                if(e.KeyCode == Keys.Escape) ToggleSettings();
                else if(e.KeyCode == Keys.Q) menu.CyclePage(-1);
                else if(e.KeyCode == Keys.E) menu.CyclePage(1);
                else if(e.KeyCode == Keys.Up) menu.Select(-1);
                else if(e.KeyCode == Keys.Down) menu.Select(1);
                else if(e.KeyCode == Keys.Left) menu.AdjustSelected(-1);
                else if(e.KeyCode == Keys.Right || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) menu.AdjustSelected(1);
                e.Handled=true; return;
            }
            keys.Add(e.KeyCode);
            if (e.KeyCode == Keys.Escape) ReleaseMouse();
        };
        KeyPress += (_, e) => { if(menu.Open && menu.EditingText) { if(menu.EditingName) menu.EditNameCharacter(e.KeyChar); else menu.EditConnectionCharacter(e.KeyChar);e.Handled=true; } };
        KeyUp += (_, e) => { keys.Remove(e.KeyCode); if(e.KeyCode==Keys.B) sandboxToggleKeyDown=false; if(e.KeyCode==Keys.G) wireToggleKeyDown=false; if(e.KeyCode==Keys.K) weldToggleKeyDown=false; if(menu.Open) return; if(e.KeyCode==Keys.E) InteractWithDevice(); if(e.KeyCode==Keys.Y) CopyAimedBlock(); if(e.KeyCode==Keys.V) PlaceCopiedBlock(); if (e.KeyCode == Keys.C) crouchToggle=!crouchToggle; if (e.KeyCode == Keys.R) Regenerate(); if (e.KeyCode == Keys.F) HandleObjectAction(0); if (e.KeyCode == Keys.T) TriggerLightEvent(); if (e.KeyCode == Keys.P) { menu.RenderMode=menu.RenderMode==1?2:1; menu.Dirty=true; } if (e.KeyCode == Keys.D1) { menu.ResolutionIndex=0; menu.Dirty=true; } if (e.KeyCode == Keys.D2) { menu.ResolutionIndex=1; menu.Dirty=true; } if (e.KeyCode == Keys.D3) { menu.ResolutionIndex=2; menu.Dirty=true; } if(menu.MovementControlsWritable && e.KeyCode==Keys.OemOpenBrackets) { menu.WalkSpeed=Math.Max(1f,menu.WalkSpeed-0.4f); menu.Dirty=true; } if(menu.MovementControlsWritable && e.KeyCode==Keys.OemCloseBrackets) { menu.WalkSpeed=Math.Min(8f,menu.WalkSpeed+0.4f); menu.Dirty=true; } if(e.KeyCode==Keys.OemMinus) { menu.MouseSensitivity=Math.Max(0.0004f,menu.MouseSensitivity*0.85f); menu.Dirty=true; } if(e.KeyCode==Keys.Oemplus) { menu.MouseSensitivity=Math.Min(0.005f,menu.MouseSensitivity*1.15f); menu.Dirty=true; } };
        KeyUp += (_, e) => {
            if(menu.Open) return;
            if(e.KeyCode==Keys.M) { menu.MonsterDebug=!menu.MonsterDebug;menu.Dirty=true; }
            if(e.KeyCode==Keys.H) { menu.ShowDebugOverlay=!menu.ShowDebugOverlay;menu.Dirty=true; }
        };
        MouseDown += (_, e) => { if(menu.Open) { if(TryMenuPoint(e.Location,out Point point)) menu.MouseDown(point.X,point.Y); if(!menu.Open) CloseSettings(); return; } wantsCapture=true; CaptureMouse(); if(e.Button==MouseButtons.Right) rotatingHeld=true; if(e.Button==MouseButtons.Left && active) { if(menu.WireTool) { WireDevice();return; } if(menu.WeldTool) { WeldGrabbables();return; } grabYaw=grabPitch=0f; if(network.Connected) { networkHolding=true;networkHeldId=0; SendPlayerState(); network.Send(new NetMessage { Type="pickup",GrabSequence=++grabSequence }); } else { ViewPose(out Vector3 eye,out Vector3 forward); physics.HoldDistance=menu.HoldDistance; physics.TryPickup(eye,forward,ViewRotation(),boxes); } } };
        MouseMove += (_, e) => { if(menu.Open && TryMenuPoint(e.Location,out Point point)) menu.MouseMove(point.X,point.Y); };
        MouseWheel += (_, e) => { if(e.Delta==0) return; if(menu.Open) { menu.MouseWheel(e.Delta); return; } int step=Math.Sign(e.Delta); if(network.Connected) { if(networkHolding || networkCubes.Any(cube=>cube.HolderId==network.Id && network.Id!=0)) network.Send(new NetMessage { Type="grab_scroll",Action=step }); } else physics.NudgeGrabDistance(0,step*0.2f); };
        MouseUp += (_, e) => { menu.MouseUp(); if(e.Button==MouseButtons.Right) rotatingHeld=false; if(e.Button==MouseButtons.Left) { if(network.Connected) network.Send(new NetMessage { Type="release" }); else physics.Release(); networkHolding=false;networkHeldId=0;grabYaw=grabPitch=0f; } };
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
            if(menu.PonderPage)
            {
                switch(keyData)
                {
                    case Keys.Up or Keys.Left: menu.MovePonder(-1); return true;
                    case Keys.Down or Keys.Right or Keys.Enter or Keys.Space: menu.MovePonder(1); return true;
                }
            }
            if(menu.SandboxPage)
            {
                switch(keyData)
                {
                    case Keys.Up: menu.SelectSandbox(-1); return true;
                    case Keys.Down: menu.SelectSandbox(1); return true;
                    case Keys.Left: menu.CycleSandboxCategory(-1); return true;
                    case Keys.Right: menu.CycleSandboxCategory(1); return true;
                    case Keys.C: menu.CycleCubeColor(1); return true;
                    case Keys.X: HandleObjectAction(2); return true;
                    case Keys.Enter or Keys.Space: SpawnSandboxItem(menu.SelectedSandboxItem); return true;
                }
            }
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
            BeginSpawn(false);
            if(networkStarted) network.Start();
            ready=true;
            if(!networkStarted) { menu.ShowTitle(); wantsCapture=false; }
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

    int Compile(string vertex, string fragment)
    {
        int vs = Gl.CreateShader(0x8B31), fs = Gl.CreateShader(0x8B30);
        CompileOne(vs,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders",vertex)),vertex);
        CompileOne(fs,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders",fragment)),fragment);
        int program = Gl.CreateProgram();
        Gl.AttachShader(program,vs); Gl.AttachShader(program,fs); Gl.LinkProgram(program);
        Gl.GetProgramiv(program,0x8B82,out int okay);
        if (okay == 0) throw new Exception("Shader link failed: " + Gl.ProgramLog(program));
        Gl.DeleteShader(vs); Gl.DeleteShader(fs);
        return program;
    }
    static void CompileOne(int shader,string source,string name)
    {
        Gl.ShaderSource(shader,source); Gl.CompileShader(shader);
        Gl.GetShaderiv(shader,0x8B81,out int okay);
        if (okay == 0) throw new Exception(name + " shader error:\n" + Gl.ShaderLog(shader));
    }

    int LoadImage(string filename)
    {
        string path = Path.Combine(AppContext.BaseDirectory,"assets",filename);
        if (!File.Exists(path)) throw new FileNotFoundException("Missing texture",path);
        using var original = new Bitmap(path);
        using var image = new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(image)) g.DrawImageUnscaled(original,0,0);
        var data = image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        int id = Gl.GenTexture();
        Gl.BindTexture(0x0DE1,id);
        // Point-sample both texels and mip levels for the low-resolution look.
        Gl.TexParameteri(0x0DE1,0x2801,0x2700); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x2901); Gl.TexParameteri(0x0DE1,0x2803,0x2901);
        Gl.TexImage2D(0x0DE1,0,0x8C43,image.Width,image.Height,0,0x80E1,0x1401,data.Scan0);
        Gl.GenerateMipmap(0x0DE1);
        image.UnlockBits(data);
        return id;
    }

    void CreateRenderTarget()
    {
        if (renderTexture != 0) Gl.DeleteTexture(renderTexture);
        renderTexture = Gl.GenTexture(); Gl.BindTexture(0x0DE1,renderTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2703); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        Gl.TexImage2D(0x0DE1,0,0x881A,width,height,0,0x1908,0x1406,IntPtr.Zero);
        Gl.GenerateMipmap(0x0DE1);
        CreateBloomTargets();
    }

    void CreateBloomTargets()
    {
        int w=Math.Max(1,width/2),h=Math.Max(1,height/2);
        if(bloomA!=0) Gl.DeleteTexture(bloomA);
        if(bloomB!=0) Gl.DeleteTexture(bloomB);
        bloomA=CreatePostTexture(w,h,IntPtr.Zero);
        bloomB=CreatePostTexture(w,h,IntPtr.Zero);
    }
    void CreateExposureTargets()
    {
        float[] neutral=[1f,0f,0f,1f];
        var pin=GCHandle.Alloc(neutral,GCHandleType.Pinned);
        try
        {
            exposureA=CreatePostTexture(1,1,pin.AddrOfPinnedObject());
            exposureB=CreatePostTexture(1,1,pin.AddrOfPinnedObject());
        }
        finally { pin.Free(); }
        exposureCurrent=0;
        lastExposureTime=watch.Elapsed.TotalSeconds;
    }
    static int CreatePostTexture(int width,int height,IntPtr initial)
    {
        int texture=Gl.GenTexture();Gl.BindTexture(0x0DE1,texture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2601);Gl.TexParameteri(0x0DE1,0x2800,0x2601);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F);Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        Gl.TexImage2D(0x0DE1,0,0x881A,width,height,0,0x1908,0x1406,initial);
        return texture;
    }

    void CreateProbeTargets()
    {
        int wanted=menu.ProbeQuality switch { 0=>48, 1=>96, 2=>144, _=>192 };
        if(probeSize==wanted && probeDirect!=0) return;
        if(probeDirect!=0) Gl.DeleteTexture(probeDirect);
        if(probeA!=0) Gl.DeleteTexture(probeA);
        if(probeB!=0) Gl.DeleteTexture(probeB);
        probeSize=wanted;
        probeDirect=CreatePostTexture(wanted,wanted,IntPtr.Zero);
        probeA=CreatePostTexture(wanted,wanted,IntPtr.Zero);
        probeB=CreatePostTexture(wanted,wanted,IntPtr.Zero);
        probeResult=probeDirect;
        probeDirty=true;
    }

    int ProbeSettingsSignature()
    {
        var hash=new HashCode();
        hash.Add(menu.ProbeQuality);hash.Add(menu.ProbeBounces);
        hash.Add(menu.Lights);hash.Add(menu.LightIntensity);hash.Add(menu.BounceStrength);
        if(network.Connected)
        {
            foreach(CubeState cube in networkCubes)
            {
                if(!cube.Emissive) continue;
                hash.Add((int)MathF.Round(cube.X*20f));hash.Add((int)MathF.Round(cube.Y*20f));
                hash.Add((int)MathF.Round(cube.Z*20f));hash.Add(cube.HalfSize);
                hash.Add(cube.R);hash.Add(cube.G);hash.Add(cube.B);
            }
            foreach(SandboxDevice device in networkDevices)
            {
                if(device.Kind!=SandboxKind.Lamp) continue;
                hash.Add(device.Id);hash.Add(device.State);
                hash.Add((int)MathF.Round(device.X*12f));hash.Add((int)MathF.Round(device.Z*12f));
            }
        }
        else
        {
            foreach(RigidCube cube in physics.Bodies)
            {
                if(!cube.Emissive) continue;
                hash.Add((int)MathF.Round(cube.Position.X*20f));
                hash.Add((int)MathF.Round(cube.Position.Y*20f));
                hash.Add((int)MathF.Round(cube.Position.Z*20f));
                hash.Add(cube.HalfSize);hash.Add(cube.Color);
            }
            foreach(SandboxDevice device in localSandbox.Devices)
            {
                if(device.Kind!=SandboxKind.Lamp) continue;
                hash.Add(device.Id);hash.Add(device.State);
                hash.Add((int)MathF.Round(device.X*12f));hash.Add((int)MathF.Round(device.Z*12f));
            }
        }
        return hash.ToHashCode();
    }

    float CubeVisualScale(int index,int id,double now)
    {
        if(cubeVisualIds[index]!=id)
        {
            cubeVisualIds[index]=id;
            cubeVisualBirth[index]=now;
        }
        float t=Math.Clamp((float)((now-cubeVisualBirth[index])/0.32),0f,1f);
        float eased=1f-(1f-t)*(1f-t)*(1f-t);
        return 0.12f+0.88f*eased;
    }

    void UploadObjects(int program,bool animate)
    {
        RigidCube[] localCubes=network.Connected?[]:physics.Bodies.Where(body=>body.SandboxDeviceId==0).ToArray();
        int cubeCount=network.Connected?networkCubes.Length:localCubes.Length;
        SandboxDevice[] devices=network.Connected?networkDevices:localSandbox.Devices.ToArray();
        int bodyCount=cubeCount+devices.Length;
        if(cubeVisualIds.Length<cubeCount)
        {
            Array.Resize(ref cubeVisualIds,cubeCount);
            Array.Resize(ref cubeVisualBirth,cubeCount);
        }
        if(cubeVisualNetworkMode!=network.Connected || cubeCount==0)
        {
            Array.Clear(cubeVisualIds);
            cubeVisualNetworkMode=network.Connected;
        }
        Uniform1(program,"objectCount",bodyCount);
        if(bodyCount==0) return;
        if(objectTexels.Length<bodyCount*12) Array.Resize(ref objectTexels,bodyCount*12);
        float[] texels=objectTexels;
        double now=watch.Elapsed.TotalSeconds;
        for(int i=0;i<cubeCount;i++)
        {
            int j=i*12;
            if(network.Connected)
            {
                CubeState b=networkCubes[i];
                texels[j]=b.X; texels[j+1]=b.Y; texels[j+2]=b.Z;
                texels[j+3]=b.HalfSize*(animate?CubeVisualScale(i,b.Id,now):1f);
                texels[j+4]=b.Qx;texels[j+5]=b.Qy;texels[j+6]=b.Qz;texels[j+7]=b.Qw;
                texels[j+8]=b.R;texels[j+9]=b.G;texels[j+10]=b.B;
                texels[j+11]=b.Emissive?1f:b.Plank?-1f:0f;
            }
            else
            {
                RigidCube b=localCubes[i];
                texels[j]=b.Position.X; texels[j+1]=b.Position.Y; texels[j+2]=b.Position.Z;
                texels[j+3]=b.HalfSize*(animate?CubeVisualScale(i,b.Id,now):1f);
                texels[j+4]=b.Rotation.X; texels[j+5]=b.Rotation.Y; texels[j+6]=b.Rotation.Z; texels[j+7]=b.Rotation.W;
                texels[j+8]=b.Color.X; texels[j+9]=b.Color.Y; texels[j+10]=b.Color.Z;
                texels[j+11]=b.Emissive?1f:b.Plank?-1f:0f;
            }
        }
        for(int i=0;i<devices.Length;i++)
        {
            SandboxDevice device=devices[i];int j=(cubeCount+i)*12;
            texels[j]=device.X;texels[j+1]=device.Y;texels[j+2]=device.Z;texels[j+3]=0.5f;
            Quaternion q=device.Rotation;
            if(q.LengthSquared()<0.001f) q=Quaternion.CreateFromAxisAngle(Vector3.UnitY,device.Yaw);
            texels[j+4]=q.X;texels[j+5]=q.Y;texels[j+6]=q.Z;texels[j+7]=q.W;
            Vector3 color=device.Kind switch
            {
                SandboxKind.Thruster=>new(0.55f,0.62f,0.68f),
                SandboxKind.Button=>new(0.75f,0.19f,0.12f),
                SandboxKind.Lever=>new(0.65f,0.68f,0.62f),
                SandboxKind.Lamp=>new(0.95f,0.83f,0.44f),
                SandboxKind.Piston=>new(0.30f,0.34f,0.38f),
                SandboxKind.PressurePlate=>new(0.78f,0.45f,0.16f),
                SandboxKind.ProximitySensor=>new(0.26f,0.83f,0.90f),
                SandboxKind.Delay=>new(0.52f,0.36f,0.82f),
                SandboxKind.Counter=>new(0.91f,0.37f,0.27f),
                SandboxKind.Randomizer=>new(0.38f,0.74f,0.40f),
                SandboxKind.Hinge=>new(0.72f,0.48f,0.18f),
                SandboxKind.Wheel=>new(0.18f,0.20f,0.24f),
                SandboxKind.Rope=>new(0.62f,0.42f,0.20f),
                SandboxKind.Conveyor=>new(0.22f,0.29f,0.34f),
                SandboxKind.SensorLight=>new(0.28f,0.92f,0.75f),
                SandboxKind.DisplayPanel=>new(0.32f,0.70f,0.96f),
                _=>new(0.21f,0.49f,0.56f)
            };
            texels[j+8]=color.X;texels[j+9]=color.Y;texels[j+10]=color.Z;
            texels[j+11]=(int)device.Kind+2+(device.State?0.5f:0f);
        }
        int textureUnit=program==probeProgram?4:9;
        Bind(textureUnit,objectTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        var handle=GCHandle.Alloc(texels,GCHandleType.Pinned);
        try
        {
            if(objectTextureHeight!=bodyCount)
            {
                Gl.TexImage2D(0x0DE1,0,0x8814,3,bodyCount,0,0x1908,0x1406,handle.AddrOfPinnedObject());
                objectTextureHeight=bodyCount;
            }
            else Gl.TexSubImage2D(0x0DE1,0,0,0,3,bodyCount,0x1908,0x1406,handle.AddrOfPinnedObject());
        }
        finally { handle.Free(); }
        Uniform1(program,"objectMap",textureUnit);
    }

    void RenderProbes()
    {
        CreateProbeTargets();
        Gl.BindFramebuffer(0x8D40,framebuffer);
        Gl.Viewport(0,0,probeSize,probeSize);
        Gl.UseProgram(probeProgram);
        Bind(0,worldTexture);Bind(1,fixtureTexture);
        Bind(2,0);Bind(3,0);
        Uniform1(probeProgram,"worldGrid",0);
        Uniform1(probeProgram,"fixtureMap",1);
        Uniform1(probeProgram,"directTex",2);
        Uniform1(probeProgram,"previousTex",3);
        Uniform1(probeProgram,"probeSize",probeSize);
        Uniform1(probeProgram,"probeRadius",menu.ProbeQuality==0?1:2);
        Uniform1(probeProgram,"probeLightSamples",menu.ProbeQuality==0?1:menu.ProbeQuality==1?2:4);
        Uniform1(probeProgram,"lightsEnabled",menu.Lights?1:0);
        Uniform1f(probeProgram,"lightIntensity",menu.LightIntensity);
        Uniform1f(probeProgram,"bounceStrength",menu.BounceStrength);
        UploadObjects(probeProgram,false);
        Uniform1(probeProgram,"passIndex",0);
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,probeDirect,0);
        Gl.DrawArrays(0x0004,0,3);
        int previous=probeDirect;
        for(int bounce=0;bounce<menu.ProbeBounces;bounce++)
        {
            int target=(bounce&1)==0?probeA:probeB;
            Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,target,0);
            Bind(2,probeDirect);Bind(3,previous);
            Uniform1(probeProgram,"passIndex",1);
            Gl.DrawArrays(0x0004,0,3);
            previous=target;
        }
        probeResult=previous;
        probeDirty=false;
    }

    void ResizeRender(int w,int h) { width=w; height=h; CreateRenderTarget(); }

    void GenerateWorld()
    {
        probeDirty=true;
        physics.Clear();
        localSandbox.Clear();networkDevices=[];networkWires=[];pendingWireId=0;pendingWeldId=0;
        var layout=WorldLayout.Generate(seed);
        grid=layout.Grid;
        boxes.Clear();
        boxes.AddRange(layout.Walls);
        foreach(var plank in WorldLayout.CornerPlanks(seed,boxes))
            physics.SpawnCornerPlank(plank.Position,plank.Yaw,boxes,camera);
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
        menu.BodyCount=physics.Bodies.Count(body=>body.SandboxDeviceId==0); menu.Dirty=true;
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
        if(network.Connected)
        {
            if(network.Id==menu.NetworkHost) network.Send(new NetMessage { Type="regen" });
        }
        else { seed = Random.Shared.Next(1,1_000_000_000); GenerateWorld(); motion.Reset(); BeginSpawn(false); }
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
    void ToggleSettings()
    {
        bool opening=!menu.Open;
        networkHolding=false;

        network.Send(new NetMessage { Type="release" });
        physics.Release();
        menu.Toggle();
        if(opening && !menu.TitleScreen) menu.SetPage(9);
        if(menu.Open) { wantsCapture=false; SuspendMouse(); }
        else CloseSettings();
    }
    void CloseSettings()
    {
        wantsCapture=true;
        BeginInvoke(new Action(CaptureMouse));
    }
    void ConnectOnline()
    {
        menu.FinishEditing();
        if(string.IsNullOrWhiteSpace(menu.JoinKey))
        {
            menu.NetworkStatus="ENTER KEY"; menu.Dirty=true;
            return;
        }
        DisconnectNetwork();
        network=new PhotonNetClient(menu.JoinKey);
        networkStarted=true;
        remoteServer=true;
        menu.NetworkRemote=true;
        menu.NetworkStatus="CONNECTING";
        menu.Dirty=true;
        network.Start();
    }
    void DisconnectNetwork()
    {
        network.Dispose();
        network=new LocalNetClient();
        networkStarted=false;
        remoteServer=false;
        networkHolding=false;

        menu.NetworkRemote=false;
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
    void HandleObjectAction(int action)
    {
        if(action==1 && !menu.SandboxEnabled) return;
        if(network.Connected)
        {
            SendPlayerState();
            network.Send(new NetMessage { Type=action==2?"clear":"spawn",Action=action,
                R=menu.CubeColor.X,G=menu.CubeColor.Y,B=menu.CubeColor.Z,Material=menu.CubeMaterial });
            return;
        }
        if(action==2) { physics.Clear();localSandbox.Clear(); }
        else
        {
            ViewPose(out Vector3 eye,out Vector3 forward);
            physics.Spawn(eye,forward,action==1,boxes,camera,menu.CubeColor,menu.CubeMaterial);
        }
        menu.BodyCount=physics.Bodies.Count(body=>body.SandboxDeviceId==0);
        menu.Dirty=true;
    }

    void TogglePonderScreen()
    {
        network.Send(new NetMessage { Type="release" });physics.Release();networkHolding=false;
        menu.TogglePonder();
        if(menu.Open) { wantsCapture=false;SuspendMouse(); }
        else CloseSettings();
    }

    void ToggleSandboxMenu()
    {
        if(!menu.SandboxPage && !menu.SandboxEnabled) return;
        network.Send(new NetMessage { Type="release" });physics.Release();networkHolding=false;
        menu.ToggleSandbox();
        if(menu.Open) { wantsCapture=false;SuspendMouse(); }
        else CloseSettings();
    }

    void ToggleSandboxPermission()
    {
        if(network.Connected)
        {
            if(network.SupportsSandbox) network.Send(new NetMessage { Type="sandbox_toggle" });
            return;
        }
        menu.SandboxEnabled=!menu.SandboxEnabled;
        menu.Dirty=true;
    }

    void SpawnSandboxItem(SandboxKind kind)
    {
        if(!menu.SandboxEnabled && kind!=SandboxKind.Cube) return;
        if(kind==SandboxKind.Cube) { HandleObjectAction(0);return; }
        if(kind==SandboxKind.GlowCube) { HandleObjectAction(1);return; }
        if(network.Connected)
        { SendPlayerState();network.Send(new NetMessage { Type="device_spawn",Action=(int)kind });return; }
        ViewPose(out Vector3 eye,out Vector3 forward);
        SandboxDevice? device=localSandbox.Spawn(kind,eye,forward,smoothYaw+motion.YawOffset,boxes);
        if(device!=null && !physics.SpawnSandboxDevice(device,boxes,camera)) localSandbox.Remove(device.Id);
        menu.Dirty=true;
    }

    SandboxDevice? AimedDevice()
    {
        ViewPose(out Vector3 eye,out Vector3 forward);
        if(!network.Connected) return localSandbox.Aim(eye,forward);
        SandboxDevice? best=null;float distance=6f;
        foreach(SandboxDevice device in networkDevices)
        {
            Vector3 delta=device.Position-eye;
            float along=Vector3.Dot(delta,forward);
            if(along<0 || along>distance || Vector3.DistanceSquared(delta,forward*along)>0.42f*0.42f) continue;
            best=device;distance=along;
        }
        return best;
    }

    void InteractWithDevice()
    {
        if(!menu.SandboxEnabled) return;
        SandboxDevice? device=AimedDevice();
        if(device==null) return;
        if(network.Connected) network.Send(new NetMessage { Type="device_interact",Action=device.Id });
        else localSandbox.Interact(device.Id);
    }

    void WireDevice()
    {
        if(!menu.SandboxEnabled) return;
        SandboxDevice? device=AimedDevice();
        if(device==null) { pendingWireId=0;return; }
        if(pendingWireId==0) { pendingWireId=device.Id;return; }
        if(network.Connected) network.Send(new NetMessage { Type="wire",FromId=pendingWireId,ToId=device.Id });
        else localSandbox.Connect(pendingWireId,device.Id);
        pendingWireId=0;
    }

    static Vector3 CubeLocalExtents(CubeState cube)
        => cube.Plank ? new Vector3(.90f,.11f,.90f) : new Vector3(cube.HalfSize);

    int AimedGrabbable()
    {
        ViewPose(out Vector3 eye,out Vector3 forward);
        if(!network.Connected)
        {
            float nearest=MathF.Min(menu.PickupRange,PhysicsBodies.WallRay(eye,forward,boxes,menu.PickupRange));
            int id=0;
            foreach(RigidCube body in physics.Bodies)
            {
                Quaternion inverse=Quaternion.Inverse(body.Rotation);
                float hit=PhysicsBodies.RayBox(Vector3.Transform(eye-body.Position,inverse),Vector3.Transform(forward,inverse),Vector3.Zero,PhysicsBodies.LocalExtents(body));
                if(hit>=0f&&hit<nearest) { nearest=hit;id=body.Id; }
            }
            return id;
        }
        float distance=MathF.Min(menu.PickupRange,PhysicsBodies.WallRay(eye,forward,boxes,menu.PickupRange));
        int selected=0;
        foreach(CubeState cube in networkCubes)
        {
            Quaternion rotation=new(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
            if(rotation.LengthSquared()<.001f) rotation=Quaternion.Identity;
            Quaternion inverse=Quaternion.Inverse(rotation);
            float hit=PhysicsBodies.RayBox(Vector3.Transform(eye-new Vector3(cube.X,cube.Y,cube.Z),inverse),Vector3.Transform(forward,inverse),Vector3.Zero,CubeLocalExtents(cube));
            if(hit>=0f&&hit<distance) { distance=hit;selected=cube.Id; }
        }
        foreach(SandboxDevice device in networkDevices)
        {
            Quaternion rotation=device.Rotation;
            if(rotation.LengthSquared()<.001f) rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,device.Yaw);
            Quaternion inverse=Quaternion.Inverse(rotation);
            float hit=PhysicsBodies.RayBox(Vector3.Transform(eye-device.Position,inverse),Vector3.Transform(forward,inverse),Vector3.Zero,new Vector3(.35f));
            if(hit>=0f&&hit<distance) { distance=hit;selected=device.BodyId; }
        }
        return selected;
    }

    void WeldGrabbables()
    {
        int id=AimedGrabbable();
        if(id==0) { pendingWeldId=0;return; }
        if(pendingWeldId==0) { pendingWeldId=id;return; }
        if(network.Connected) network.Send(new NetMessage { Type="weld",FromId=pendingWeldId,ToId=id });
        else physics.TryWeld(pendingWeldId,id);
        pendingWeldId=0;
    }

    void CopyAimedBlock()
    {
        if(!menu.SandboxEnabled) return;
        SandboxDevice? device=AimedDevice();
        if(device!=null)
        {
            copiedDeviceKind=device.Kind;
            copiedCube=false;
            menu.ToolStatus=$"COPIED {device.Kind.ToString().ToUpperInvariant()} • V PLACE";
            menu.Dirty=true;
            return;
        }
        int id=AimedGrabbable();
        if(id==0) return;
        if(network.Connected)
        {
            CubeState? cube=networkCubes.FirstOrDefault(item=>item.Id==id);
            if(cube==null||cube.Plank) return;
            copiedCube=true;copiedEmissive=cube.Emissive;copiedMaterial=cube.Material;copiedColor=new Vector3(cube.R,cube.G,cube.B);copiedDeviceKind=null;
        }
        else
        {
            RigidCube? cube=physics.Bodies.FirstOrDefault(item=>item.Id==id);
            if(cube==null||cube.Plank||cube.SandboxDeviceId!=0) return;
            copiedCube=true;copiedEmissive=cube.Emissive;copiedMaterial=cube.Material;copiedColor=cube.Color;copiedDeviceKind=null;
        }
        menu.ToolStatus=$"COPIED {(copiedEmissive?"GLOW ":"")}CUBE • V PLACE";
        menu.Dirty=true;
    }

    void PlaceCopiedBlock()
    {
        if(!menu.SandboxEnabled) return;
        if(copiedDeviceKind is SandboxKind kind)
        {
            if(network.Connected)
            {
                SendPlayerState();
                network.Send(new NetMessage { Type="copy_device",Action=(int)kind });
            }
            else
            {
                ViewPose(out Vector3 eye,out Vector3 forward);
                SandboxDevice? device=localSandbox.Spawn(kind,eye,forward,smoothYaw+motion.YawOffset,boxes);
                if(device!=null&&!physics.SpawnSandboxDevice(device,boxes,camera)) localSandbox.Remove(device.Id);
            }
            return;
        }
        if(!copiedCube) return;
        if(network.Connected)
        {
            SendPlayerState();
            network.Send(new NetMessage { Type="copy_cube",Action=copiedEmissive?1:0,R=copiedColor.X,G=copiedColor.Y,B=copiedColor.Z,Material=copiedMaterial });
        }
        else
        {
            ViewPose(out Vector3 eye,out Vector3 forward);
            physics.Spawn(eye,forward,copiedEmissive,boxes,camera,copiedColor,copiedMaterial);
            menu.BodyCount=physics.Bodies.Count(body=>body.SandboxDeviceId==0);
            menu.Dirty=true;
        }
    }

    void TriggerLightEvent()
    {
        if(network.Connected)
        {
            if(network.Id==menu.NetworkHost) network.Send(new NetMessage { Type="flicker" });
        }
        else flicker.Trigger(watch.Elapsed.TotalSeconds);
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
        bool movementWritable=!network.Connected && !remoteServer;
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
        if(menu.TitleScreen && menu.TitlePage==1 && network.Connected)
        { menu.LeaveTitle();CloseSettings(); }
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
        }
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
            SandboxDevice[] devices=snapshot.Devices;
            if(networkDevices.Length!=devices.Length || devices.Where((device,index)=>device.Id!=networkDevices[index].Id).Any())
                networkDevices=devices.Select(CloneDevice).ToArray();
            else for(int i=0;i<devices.Length;i++)
            {
                SandboxDevice target=devices[i],current=networkDevices[i];
                float deviceBlend=1f-MathF.Exp(-24f*dt);
                current.X+=(target.X-current.X)*deviceBlend;
                current.Y+=(target.Y-current.Y)*deviceBlend;
                current.Z+=(target.Z-current.Z)*deviceBlend;
                Quaternion currentRotation=current.Rotation,targetRotation=target.Rotation;
                if(currentRotation.LengthSquared()<.001f) currentRotation=Quaternion.Identity;
                if(targetRotation.LengthSquared()<.001f) targetRotation=Quaternion.Identity;
                Quaternion rotation=Quaternion.Slerp(currentRotation,targetRotation,deviceBlend);
                current.Qx=rotation.X;current.Qy=rotation.Y;current.Qz=rotation.Z;current.Qw=rotation.W;
                current.Yaw=target.Yaw;current.BodyId=target.BodyId;current.State=target.State;
                current.PressTime=target.PressTime;current.Kind=target.Kind;
            }
            networkWires=snapshot.Wires;
            // Older servers omit HostId. A populated player list still gives a
            // stable host identity while the protocol check surfaces the upgrade.
            int hostId=snapshot.HostId;
            if(hostId==0 && snapshot.Players.Length>0) hostId=snapshot.Players.Min(player=>player.Id);
            bool sandboxHost=hostId!=0&&hostId==network.Id;
            bool sandboxSupported=network.SupportsSandbox;
            if(menu.SandboxEnabled!=snapshot.SandboxEnabled || menu.SandboxHost!=sandboxHost ||
               menu.SandboxSupported!=sandboxSupported)
            { menu.SandboxEnabled=snapshot.SandboxEnabled;menu.SandboxHost=sandboxHost;
                menu.SandboxSupported=sandboxSupported;menu.Dirty=true; }
            int count=snapshot.Players.Length;
            if(!menu.NetworkConnected || menu.NetworkId!=network.Id || menu.NetworkHost!=hostId || menu.NetworkPlayers!=count)
            { menu.NetworkConnected=true;menu.NetworkId=network.Id;menu.NetworkHost=hostId;menu.NetworkPlayers=count;menu.Dirty=true; }
            bool writable=sandboxHost;
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
        networkDevices=[];networkWires=[];
        if(!menu.SandboxHost || !menu.SandboxSupported)
        { menu.SandboxHost=true;menu.SandboxSupported=true;menu.SandboxEnabled=false;menu.Dirty=true; }
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
        physics.SyncSandboxDevices(localSandbox.Devices);
        localSandbox.Update(dt,device=>physics.ApplyThruster(device.Position,device.Yaw,dt,device.Id),
            device=>physics.ApplyPiston(device.Position,device.Rotation,dt,device.Id),
            device=>physics.DevicePressed(device),device=>Vector3.DistanceSquared(device.Position,camera)<3f*3f,
            device=>physics.ApplyMechanism(device,dt));
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
    static SandboxDevice CloneDevice(SandboxDevice source) => new()
    {
        Id=source.Id,Kind=source.Kind,X=source.X,Y=source.Y,Z=source.Z,Yaw=source.Yaw,
        Qx=source.Qx,Qy=source.Qy,Qz=source.Qz,Qw=source.Qw,BodyId=source.BodyId,
        State=source.State,PressTime=source.PressTime
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

    void Bind(int unit,int texture) { Gl.ActiveTexture((uint)(0x84C0+unit)); Gl.BindTexture(0x0DE1,texture); }
    static void Uniform1(int p,string name,int value) { Gl.Uniform1i(Gl.GetUniformLocation(p,name),value); }
    static void Uniform1f(int p,string name,float value) { Gl.Uniform1f(Gl.GetUniformLocation(p,name),value); }
    static void Uniform2(int p,string name,float x,float y) { Gl.Uniform2f(Gl.GetUniformLocation(p,name),x,y); }
    static bool ProjectDebug(Vector3 point,Vector3 eye,Vector3 right,Vector3 up,Vector3 forward,out PointF screen)
    {
        Vector3 delta=point-eye;
        float depth=Vector3.Dot(delta,forward);
        if(depth<0.25f) { screen=default;return false; }
        float x=160f+Vector3.Dot(delta,right)*240f/(depth*1.55f);
        float y=120f-Vector3.Dot(delta,up)*240f/(depth*1.55f);
        screen=new PointF(x,y);
        return x>=-40f&&x<=360f&&y>=-40f&&y<=280f;
    }
    static bool ProjectCubeFrame(Vector3 position,Quaternion rotation,float half,
        Vector3 eye,Vector3 right,Vector3 up,Vector3 forward,out RectangleF bounds)
    {
        float minX=float.PositiveInfinity,minY=float.PositiveInfinity;
        float maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
        for(int sx=-1;sx<=1;sx+=2) for(int sy=-1;sy<=1;sy+=2) for(int sz=-1;sz<=1;sz+=2)
        {
            Vector3 corner=position+Vector3.Transform(new Vector3(sx*half,sy*half,sz*half),rotation);
            if(Vector3.Dot(corner-eye,forward)<0.25f) { bounds=default;return false; }
            ProjectDebug(corner,eye,right,up,forward,out PointF screen);
            minX=MathF.Min(minX,screen.X);minY=MathF.Min(minY,screen.Y);
            maxX=MathF.Max(maxX,screen.X);maxY=MathF.Max(maxY,screen.Y);
        }
        if(maxX<0f||minX>320f||maxY<0f||minY>240f) { bounds=default;return false; }
        int left=Math.Clamp((int)MathF.Floor(minX)-1,1,316);
        int top=Math.Clamp((int)MathF.Floor(minY)-1,1,236);
        int rightEdge=Math.Clamp((int)MathF.Ceiling(maxX)+1,left+2,319);
        int bottomEdge=Math.Clamp((int)MathF.Ceiling(maxY)+1,top+2,239);
        bounds=RectangleF.FromLTRB(left,top,rightEdge,bottomEdge);
        return true;
    }
    void PrepareMonsterOverlay(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        if(visibleMonster==null) { menu.MonsterTargetText="TARGET NONE";menu.MonsterSegments=[];menu.MonsterPoints=[];return; }
        if(!menu.MonsterSettings.Enabled)
        {
            menu.MonsterSegments=[];menu.MonsterPoints=[];
            menu.MonsterTargetText="MONSTER OFF";menu.Dirty=true;
            return;
        }
        MonsterState monster=visibleMonster;
        menu.MonsterTargetText=monster.TargetId==0?"TARGET NONE":
            monster.Mode==2?$"TARGET P{monster.TargetId}":$"LAST P{monster.TargetId}";
        if(!menu.MonsterDebug||!menu.ShowDebugOverlay) { menu.MonsterSegments=[];menu.MonsterPoints=[];return; }
        var segments=new List<(PointF A,PointF B,Color Ink)>();
        var points=new List<(PointF At,string Label,Color Ink)>();
        void Mark(Vector3 position,string label,Color ink)
        {
            if(ProjectDebug(position,eye,right,up,forward,out PointF screen)) points.Add((screen,label,ink));
        }
        void Segment(Vector3 from,Vector3 to,Color ink)
        {
            if(ProjectDebug(from,eye,right,up,forward,out PointF a)&&
               ProjectDebug(to,eye,right,up,forward,out PointF b)) segments.Add((a,b,ink));
        }
        Vector3 head=new(monster.X,1.55f,monster.Z);
        Vector3 previous=new(monster.X,0.3f,monster.Z);
        Color routeInk=Color.FromArgb(75,225,155);
        foreach(NavPoint waypoint in monster.Route)
        {
            Vector3 next=new(waypoint.X,0.3f,waypoint.Z);
            Segment(previous,next,routeInk);
            Mark(next,".",routeInk);
            previous=next;
        }
        Mark(head,"EYE",Color.FromArgb(255,100,105));
        Mark(new Vector3(monster.GoalX,0.35f,monster.GoalZ),"GOAL",Color.FromArgb(90,205,255));
        if(monster.HasLastSeen)
            Mark(new Vector3(monster.LastX,0.35f,monster.LastZ),"LAST",Color.FromArgb(255,190,85));
        if(monster.TargetId>0 && monster.Mode==2)
        {
            Vector3? targetPlayer=null;
            if(!network.Connected||monster.TargetId==network.Id) targetPlayer=camera;
            else if(remotePositions.TryGetValue(monster.TargetId,out Vector3 remote)) targetPlayer=remote;
            if(targetPlayer.HasValue)
            {
                Vector3 player=new(targetPlayer.Value.X,1.5f,targetPlayer.Value.Z);
                Segment(head,player,Color.FromArgb(255,100,105));
                Mark(player,$"P{monster.TargetId}",Color.FromArgb(255,100,105));
            }
        }
        menu.MonsterSegments=segments.ToArray();menu.MonsterPoints=points.ToArray();
        menu.Dirty=true;
    }
    bool ClearNameSight(Vector3 from,Vector3 to)
    {
        Vector2 start=new(from.X,from.Z), end=new(to.X,to.Z), delta=end-start;
        foreach(RectangleF wall in boxes)
        {
            float lo=0f,hi=1f;
            if(!Slab(start.X,delta.X,wall.Left,wall.Right,ref lo,ref hi) ||
               !Slab(start.Y,delta.Y,wall.Top,wall.Bottom,ref lo,ref hi)) continue;
            if(lo<0.98f && hi>0.02f) return false;
        }
        return true;
    }
    static bool Slab(float start,float delta,float min,float max,ref float lo,ref float hi)
    {
        if(MathF.Abs(delta)<0.00001f) return start<min||start>max?false:true;
        float a=(min-start)/delta,b=(max-start)/delta;
        lo=MathF.Max(lo,MathF.Min(a,b));hi=MathF.Min(hi,MathF.Max(a,b));
        return lo<=hi;
    }
    void PreparePlayerTags(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        if(remotePositions.Count==0)
        { if(menu.PlayerTags.Length>0) { menu.PlayerTags=[];menu.Dirty=true; } return; }
        var tags=new List<(PointF At,string Label,Color Ink)>();
        foreach(var player in remotePositions.Take(4))
        {
            Vector3 head=player.Value+new Vector3(0,0.53f,0);
            if(Vector3.DistanceSquared(eye,head)>400f || !ClearNameSight(eye,head) ||
               !ProjectDebug(head,eye,right,up,forward,out PointF screen)) continue;
            Vector3 c=remoteColors.TryGetValue(player.Key,out Vector3 color)?color:Vector3.One;
            tags.Add((screen,remoteNames.TryGetValue(player.Key,out string? name)?name:$"BEAN{player.Key}",
                Color.FromArgb((int)(Math.Clamp(c.X,0f,1f)*255),(int)(Math.Clamp(c.Y,0f,1f)*255),(int)(Math.Clamp(c.Z,0f,1f)*255))));
        }
        menu.PlayerTags=tags.ToArray();menu.Dirty=true;
    }
    void PrepareObjectFrames(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        // Cubes are packed first and devices after, matching UploadObjects. Only cubes
        // own a birth-scale slot, so the device tail must never index CubeVisualScale.
        RigidCube[] localCubes=network.Connected?[]:physics.Bodies.Where(body=>body.SandboxDeviceId==0).ToArray();
        RigidCube[] localDevices=network.Connected?[]:physics.Bodies.Where(body=>body.SandboxDeviceId!=0).ToArray();
        int cubeCount=network.Connected?networkCubes.Length:localCubes.Length;
        int deviceCount=network.Connected?networkDevices.Length:localDevices.Length;
        int count=cubeCount+deviceCount;
        if(!menu.ShowObjectFrames||count==0)
        { if(menu.ObjectFrames.Length>0) { menu.ObjectFrames=[];menu.Dirty=true; } return; }
        int heldId=network.Connected
            ? networkHolding?networkHeldId:networkCubes.FirstOrDefault(cube=>cube.HolderId==network.Id)?.Id??0
            : physics.Held?.Id??0;
        float nearest=heldId==0
            ? MathF.Min(menu.PickupRange,PhysicsBodies.WallRay(eye,forward,boxes,menu.PickupRange))
            : float.PositiveInfinity;
        Vector3 hitPosition=default;
        Quaternion hitRotation=Quaternion.Identity;
        float hitHalf=0f;
        bool found=false;
        double now=watch.Elapsed.TotalSeconds;
        for(int i=0;i<count;i++)
        {
            bool isDevice=i>=cubeCount;
            Vector3 position;
            float half;
            int id;
            Quaternion rotation;
            float scale=1f;
            if(network.Connected)
            {
                if(!isDevice)
                {
                    CubeState cube=networkCubes[i];
                    position=new Vector3(cube.X,cube.Y,cube.Z);
                    half=cube.HalfSize;id=cube.Id;
                    rotation=new Quaternion(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
                    scale=CubeVisualScale(i,id,now);
                }
                else
                {
                    SandboxDevice device=networkDevices[i-cubeCount];
                    position=device.Position;half=.35f;id=device.BodyId;rotation=device.Rotation;
                }
                rotation=rotation.LengthSquared()<0.001f?Quaternion.Identity:Quaternion.Normalize(rotation);
            }
            else if(!isDevice)
            {
                RigidCube cube=localCubes[i];
                position=cube.Position;half=cube.HalfSize;id=cube.Id;
                rotation=cube.Rotation;
                scale=CubeVisualScale(i,id,now);
            }
            else
            {
                RigidCube cube=localDevices[i-cubeCount];
                position=cube.Position;half=cube.HalfSize;id=cube.Id;
                rotation=cube.Rotation;
            }
            if(heldId!=0 && id!=heldId) continue;
            Quaternion inverse=Quaternion.Inverse(rotation);
            float t=PhysicsBodies.RayBox(Vector3.Transform(eye-position,inverse),
                Vector3.Transform(forward,inverse),Vector3.Zero,new Vector3(half));
            if(heldId==0 && (t<0f||t>=nearest)) continue;
            nearest=t;hitPosition=position;hitRotation=rotation;
            hitHalf=half*scale;found=true;
        }
        menu.ObjectFrames=found&&ProjectCubeFrame(hitPosition,hitRotation,hitHalf,eye,right,up,forward,out RectangleF bounds)
            ?[bounds]:[];
        menu.Dirty=true;
    }
    void PrepareSandboxWires(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        IReadOnlyList<SandboxDevice> devices=network.Connected?networkDevices:localSandbox.Devices;
        IReadOnlyList<SandboxWire> wires=network.Connected?networkWires:localSandbox.Wires;
        if(wires.Count==0) { if(menu.SandboxWires.Length>0) { menu.SandboxWires=[];menu.Dirty=true; } return; }
        var byId=devices.ToDictionary(device=>device.Id);
        var segments=new List<(PointF A,PointF B,Color Ink)>();
        foreach(SandboxWire wire in wires)
        {
            if(!byId.TryGetValue(wire.From,out SandboxDevice? from) || !byId.TryGetValue(wire.To,out SandboxDevice? to)) continue;
            Vector3 start=from.Position+Vector3.UnitY*.22f;
            Vector3 end=to.Position+Vector3.UnitY*.22f;
            float da=Vector3.Dot(start-eye,forward),db=Vector3.Dot(end-eye,forward);
            if(da<0.25f && db<0.25f) continue;
            if(da<0.25f) start=Vector3.Lerp(start,end,(0.25f-da)/(db-da));
            else if(db<0.25f) end=Vector3.Lerp(end,start,(0.25f-db)/(da-db));
            PointF a=ProjectWirePoint(start,eye,right,up,forward);
            PointF b=ProjectWirePoint(end,eye,right,up,forward);
            segments.Add((a,b,from.State?Color.FromArgb(82,232,156):Color.FromArgb(104,126,139)));
        }
        menu.SandboxWires=segments.ToArray();menu.Dirty=true;
    }
    static PointF ProjectWirePoint(Vector3 point,Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        Vector3 delta=point-eye;
        float depth=MathF.Max(0.25f,Vector3.Dot(delta,forward));
        return new PointF(160f+Vector3.Dot(delta,right)*240f/(depth*1.55f),
            120f-Vector3.Dot(delta,up)*240f/(depth*1.55f));
    }
    void UploadOverlay()
    {
        using var image = menu.Draw();
        var data = image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            Bind(1,overlayTexture);
            Gl.TexImage2D(0x0DE1,0,0x8058,image.Width,image.Height,0,0x80E1,0x1401,data.Scan0);
        }
        finally { image.UnlockBits(data); }
    }
    void RunPostProcessing()
    {
        double now=watch.Elapsed.TotalSeconds;
        float dt=(float)Math.Clamp(now-lastExposureTime,0.0,0.1);
        lastExposureTime=now;
        if(menu.PostEnabled && menu.AutoExposure)
        {
            Bind(0,sceneTexture);
            Gl.GenerateMipmap(0x0DE1);
            int previous=exposureCurrent==0?exposureA:exposureB;
            int next=exposureCurrent==0?exposureB:exposureA;
            Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,next,0);
            Gl.Viewport(0,0,1,1);
            Gl.UseProgram(exposureProgram);
            Bind(0,sceneTexture);Uniform1(exposureProgram,"sceneTex",0);
            Bind(1,previous);Uniform1(exposureProgram,"previousExposure",1);
            Uniform1f(exposureProgram,"brightenSpeed",menu.BrightenSpeed);
            Uniform1f(exposureProgram,"darkenSpeed",menu.DarkenSpeed);
            Uniform1f(exposureProgram,"deltaTime",dt);
            Gl.DrawArrays(0x0004,0,3);
            exposureCurrent=1-exposureCurrent;
        }
        if(menu.PostEnabled && menu.Bloom && menu.BloomStrength>0.001f)
        {
            int w=Math.Max(1,width/2),h=Math.Max(1,height/2);
            Gl.Viewport(0,0,w,h);
            Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,bloomA,0);
            Gl.UseProgram(bloomExtractProgram);
            Bind(0,sceneTexture);Uniform1(bloomExtractProgram,"sceneTex",0);
            Uniform1f(bloomExtractProgram,"threshold",menu.BloomThreshold);
            Gl.DrawArrays(0x0004,0,3);
            BlurBloom(bloomA,bloomB,1f,0f,w,h);
            BlurBloom(bloomB,bloomA,0f,1f,w,h);
        }
        Gl.BindFramebuffer(0x8D40,0);
    }
    void BlurBloom(int source,int destination,float dx,float dy,int w,int h)
    {
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,destination,0);
        Gl.UseProgram(bloomBlurProgram);
        Bind(0,source);Uniform1(bloomBlurProgram,"sourceTex",0);
        Uniform2(bloomBlurProgram,"direction",dx,dy);
        Uniform2(bloomBlurProgram,"texelSize",1f/w,1f/h);
        Uniform1f(bloomBlurProgram,"radius",menu.BloomRadius);
        Gl.DrawArrays(0x0004,0,3);
    }
    void Render()
    {
        if(menu.LightDensity!=appliedLightDensity) RebuildFixtureMap();
        UpdateFixtureFlicker();
        if(menu.Vsync != appliedVsync)
        {
            SetVsync(menu.Vsync);
            appliedVsync=menu.Vsync;
        }
        if(menu.ResolutionIndex != appliedResolution)
        {
            appliedResolution=menu.ResolutionIndex;
            int scale=appliedResolution+1;
            ResizeRender(scale==1?320:scale==2?480:640,scale==1?240:scale==2?360:480);
        }
        if(menu.RenderMode==1)
        {
            int settings=ProbeSettingsSignature();
            if(settings!=lastProbeSettings) probeDirty=true;
            if(probeDirty) { RenderProbes();lastProbeSettings=settings; }
        }
        Gl.BindFramebuffer(0x8D40,framebuffer);
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,renderTexture,0);
        Gl.Viewport(0,0,width,height);
        Gl.UseProgram(pathProgram);
        Bind(0,worldTexture); Bind(1,wallTexture); Bind(2,floorTexture); Bind(3,ceilingTexture); Bind(4,fixtureTexture); Bind(5,faceTexture); Bind(6,eyeTexture);
        Bind(7,probeDirect);Bind(8,probeResult);
        Uniform1(pathProgram,"worldGrid",0); Uniform1(pathProgram,"wallTex",1); Uniform1(pathProgram,"floorTex",2); Uniform1(pathProgram,"ceilingTex",3);
        Uniform1(pathProgram,"fixtureMap",4);
        Uniform1(pathProgram,"faceTex",5);
        Uniform1(pathProgram,"eyeTex",6);
        Uniform1(pathProgram,"probeDirectTex",7);
        Uniform1(pathProgram,"probeBounceTex",8);
        MonsterState? monster=visibleMonster;
        Gl.Uniform4fv(Gl.GetUniformLocation(pathProgram,"monsterData"),1,
            [menu.MonsterSettings.Enabled ? monster?.X??-100f : -100f,
             monster?.Y??1.5f,monster?.Z??-100f,monster?.Yaw??0f]);
        Uniform1(pathProgram,"frameIndex",frames); Uniform1(pathProgram,"lightsEnabled",menu.Lights?1:0); Uniform1(pathProgram,"worldSeed",seed);
        Uniform1(pathProgram,"hybridMode",menu.RenderMode==1?1:0);
        Uniform1(pathProgram,"fixedPathMode",menu.RenderMode==2?1:0);
        Uniform1(pathProgram,"probeSize",probeSize);
        Uniform1(pathProgram,"hybridDirectSamples",menu.HybridDirectSamples);
        Uniform1(pathProgram,"sampleCount",menu.Samples);
        Uniform1(pathProgram,"bounceCount",menu.Bounces);
        Uniform1f(pathProgram,"bounceStrength",menu.BounceStrength);
        Uniform1f(pathProgram,"aoStrength",menu.AO);
        Uniform1f(pathProgram,"lightDensity",menu.LightDensity);
        Uniform1f(pathProgram,"lightIntensity",menu.LightIntensity);
        Uniform1(pathProgram,"volumetricsEnabled",menu.Volumetrics?1:0);
        float chaseFog=menu.FlickerSettings.ChaseFog ? chaseBlend : 0f;
        Uniform1f(pathProgram,"fogDensity",menu.FogDensity+
            (MathF.Max(menu.FogDensity,menu.FlickerSettings.ChaseFogMax)-menu.FogDensity)*chaseFog);
        Uniform1(pathProgram,"fogSteps",menu.FogSteps);
        UploadObjects(pathProgram,true);
        int remoteCount=Math.Min(4,remotePositions.Count);
        Uniform1(pathProgram,"remoteCount",remoteCount);
        if(remoteCount>0)
        {
            float[] remoteData=new float[remoteCount*4];
            float[] colors=new float[remoteCount*3];
            int i=0;
            foreach(var player in remotePositions.Take(remoteCount))
            {
                int j=i++*4;
                remoteData[j]=player.Value.X;remoteData[j+1]=player.Value.Y;
                remoteData[j+2]=player.Value.Z;remoteData[j+3]=remoteYaws[player.Key];
                Vector3 color=remoteColors.TryGetValue(player.Key,out Vector3 chosen)?chosen:new Vector3(0.78f,0.76f,0.70f);
                int k=(i-1)*3;colors[k]=color.X;colors[k+1]=color.Y;colors[k+2]=color.Z;
            }
            Gl.Uniform4fv(Gl.GetUniformLocation(pathProgram,"remoteData[0]"),remoteCount,remoteData);
            Gl.Uniform3fv(Gl.GetUniformLocation(pathProgram,"remoteColor[0]"),remoteCount,colors);
        }
        Uniform2(pathProgram,"resolution",width,height);
        ViewPose(out Vector3 viewPosition,out Vector3 forward);
        Gl.Uniform3f(Gl.GetUniformLocation(pathProgram,"cameraPos"),viewPosition.X,viewPosition.Y,viewPosition.Z);
        float viewYaw=smoothYaw+motion.YawOffset;
        float viewPitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f);
        Vector3 right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,forward));
        Vector3 up=Vector3.Normalize(Vector3.Cross(forward,right));
        float rollCos=MathF.Cos(motion.Roll), rollSin=MathF.Sin(motion.Roll);
        Vector3 rolledRight=right*rollCos+up*rollSin;
        Vector3 rolledUp=up*rollCos-right*rollSin;
        float[] basis={rolledRight.X,rolledRight.Y,rolledRight.Z,rolledUp.X,rolledUp.Y,rolledUp.Z,forward.X,forward.Y,forward.Z};
        Gl.UniformMatrix3fv(Gl.GetUniformLocation(pathProgram,"cameraBasis"),1,false,basis);
        PrepareMonsterOverlay(viewPosition,rolledRight,rolledUp,forward);
        PreparePlayerTags(viewPosition,rolledRight,rolledUp,forward);
        PrepareObjectFrames(viewPosition,rolledRight,rolledUp,forward);
        PrepareSandboxWires(viewPosition,rolledRight,rolledUp,forward);
        Gl.DrawArrays(0x0004,0,3);
        sceneTexture=renderTexture;
        RunPostProcessing();
        if(menu.Dirty) UploadOverlay();
        Gl.Viewport(0,0,FramebufferSize.Width,FramebufferSize.Height);
        Gl.UseProgram(displayProgram);
        Bind(0,sceneTexture); Uniform1(displayProgram,"accumulation",0);
        Bind(1,overlayTexture); Uniform1(displayProgram,"overlayTexture",1);
        Bind(2,bloomA);Uniform1(displayProgram,"bloomTexture",2);
        Bind(3,exposureCurrent==0?exposureA:exposureB);Uniform1(displayProgram,"exposureTexture",3);
        Uniform1(displayProgram,"ditheringEnabled",menu.Dithering?1:0);
        Uniform1(displayProgram,"paletteReduction",menu.PaletteReduction?1:0);
        Uniform1(displayProgram,"temperature",menu.Temperature);
        Uniform1(displayProgram,"paletteSteps",menu.PaletteSteps);
        (float transitionDark,float transitionFlash)=spawnTransition.Sample(watch.Elapsed.TotalSeconds);
        Uniform1f(displayProgram,"transitionDark",transitionDark);
        Uniform1f(displayProgram,"transitionFlash",transitionFlash);
        Uniform1(displayProgram,"autoExposure",menu.AutoExposure?1:0);
        Uniform1(displayProgram,"postEnabled",menu.PostEnabled?1:0);
        Uniform1(displayProgram,"bloomEnabled",menu.Bloom?1:0);
        Uniform1f(displayProgram,"bloomStrength",menu.BloomStrength);
        Uniform1(displayProgram,"tonemapMode",menu.TonemapMode);
        Uniform1f(displayProgram,"exposure",menu.Exposure);
        Uniform1f(displayProgram,"saturation",menu.Saturation);
        Uniform1f(displayProgram,"contrast",menu.Contrast);
        Gl.Uniform3f(Gl.GetUniformLocation(displayProgram,"colorMix"),menu.RedMix,menu.GreenMix,menu.BlueMix);
        Uniform2(displayProgram,"windowSize",FramebufferSize.Width,FramebufferSize.Height);
        Gl.DrawArrays(0x0004,0,3);
        frames++;
        reportFrames++;
        if (watch.Elapsed.TotalSeconds-reportTime>0.5)
        {
            double elapsed=watch.Elapsed.TotalSeconds-reportTime;
            fpsValue=(int)Math.Round(reportFrames/elapsed);
            frameMs=(float)(elapsed*1000.0/Math.Max(reportFrames,1));
            menu.FpsValue=fpsValue;
            Text=$"BEANROOMS  |  {(network.Connected?$"{(remoteServer?"ONLINE":"LOCAL")} {remotePositions.Count+1} PLAYERS  •  ":"")}{menu.RenderModeName}  •  {width}×{height}  •  {fpsValue} FPS  •  WASD move  •  F cube  •  Y copy  •  V place  •  T flicker  •  LMB carry  •  Tab menu  •  F11 full screen";
            reportFrames=0; reportTime=watch.Elapsed.TotalSeconds;
        }
        if(watch.Elapsed.TotalSeconds-lastDebugTime>=0.2)
        {
            lastDebugTime=watch.Elapsed.TotalSeconds;
            menu.DebugSeed=seed;
            menu.DebugLines=
            [
                $"FPS {fpsValue:000}  {frameMs:0.0}MS",
                $"{menu.RenderModeName.ToUpperInvariant()} {width}X{height}",
                $"SPP {menu.Samples}  GI {menu.Bounces}",
                $"SEED {seed}",
                $"XYZ {camera.X:0.0} {camera.Y:0.0} {camera.Z:0.0}",
                network.Connected?$"NET LOCAL {remotePositions.Count+1}P":"NET OFFLINE",
                $"CUBES {menu.BodyCount}",
                $"EYE {(menu.MonsterSettings.Enabled ? visibleMonster?.Mode switch { 2=>"CHASE",1=>"SEARCH",_=>"ROAM" } : "OFF")}",
                $"M VECTORS {(menu.MonsterDebug?"ON":"OFF")}",
                $"H DEBUG {(menu.ShowDebugOverlay?"ON":"OFF")}",
                $"FOG {(menu.Volumetrics?"ON":"OFF")} {menu.FogSteps}STEP"
            ];
            if(menu.ShowDebugOverlay||menu.Open) menu.Dirty=true;
        }
    }
}
