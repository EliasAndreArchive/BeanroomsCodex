using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal sealed class DebugMenu
{
    internal bool Open, TitleScreen, SettingsFromTitle, Dithering = true, PaletteReduction = true, PostEnabled = true, AutoExposure, Bloom = true, Lights = true, Vsync = true, Dirty = true, Volumetrics = true;
    internal bool SandboxPage, PonderPage, SandboxEnabled, SandboxHost=true, SandboxSupported=true, WireTool, WeldTool;
    internal int PonderStep;
    internal string ToolStatus="";
    internal int SandboxCategory, SandboxSelected, SandboxScroll;
    internal Action<SandboxKind>? SandboxSpawn;
    internal Action? SandboxToggle;
    static readonly SandboxKind[][] SandboxItems =
    [
        [SandboxKind.Cube,SandboxKind.GlowCube,SandboxKind.Pallet,SandboxKind.Slab],
        [SandboxKind.Thruster,SandboxKind.Lamp,SandboxKind.Piston,SandboxKind.PressurePlate,SandboxKind.ProximitySensor,
         SandboxKind.Hinge,SandboxKind.Wheel,SandboxKind.Rope,SandboxKind.Conveyor,SandboxKind.SensorLight,SandboxKind.DisplayPanel],
        [SandboxKind.Button,SandboxKind.Lever,SandboxKind.And,SandboxKind.Or,SandboxKind.Not,
         SandboxKind.Xor,SandboxKind.Nand,SandboxKind.Xnor,SandboxKind.Toggle,SandboxKind.Timer,
         SandboxKind.Delay,SandboxKind.Counter,SandboxKind.Randomizer]
    ];
    internal const int SandboxRows=7, SandboxPitch=14, SandboxTop=54;
    internal SandboxKind SelectedSandboxItem => SandboxItems[SandboxCategory][SandboxSelected];
    internal int SandboxItemCount => SandboxItems[SandboxCategory].Length;
    internal int SandboxMaxScroll => Math.Max(0,SandboxItemCount-SandboxRows);
    static readonly string[] SandboxCategories=["OBJECTS","DEVICES","LOGIC"];
    static string SandboxName(SandboxKind kind) => kind switch
    {
        SandboxKind.GlowCube=>"GLOW CUBE",SandboxKind.And=>"AND GATE",
        SandboxKind.Or=>"OR GATE",SandboxKind.Not=>"NOT GATE",SandboxKind.Xor=>"XOR GATE",
        SandboxKind.Nand=>"NAND GATE",SandboxKind.Xnor=>"XNOR GATE",SandboxKind.Toggle=>"TOGGLE FLIP",
        SandboxKind.Timer=>"TIMER CLOCK",SandboxKind.PressurePlate=>"PRESSURE PLATE",
        SandboxKind.ProximitySensor=>"PROXIMITY SENSOR",SandboxKind.Randomizer=>"RANDOM PULSE",
        SandboxKind.SensorLight=>"SENSOR LIGHT",SandboxKind.DisplayPanel=>"DISPLAY PANEL",SandboxKind.Pallet=>"PALLET",SandboxKind.Slab=>"CUBE SLAB",
        _=>kind.ToString().ToUpperInvariant()
    };
    static string SandboxDetail(SandboxKind kind) => kind switch
    {
        SandboxKind.Cube=>"PHYSICS BOX",SandboxKind.GlowCube=>"LIGHT BOX",
        SandboxKind.Thruster=>"POWER PUSHES CUBE",SandboxKind.Button=>"PULSE OUTPUT",
        SandboxKind.Lever=>"TOGGLE OUTPUT",SandboxKind.And=>"ALL INPUTS ON",
        SandboxKind.Or=>"ANY INPUT ON",SandboxKind.Not=>"INVERT INPUT",
        SandboxKind.Xor=>"ODD PARITY",SandboxKind.Nand=>"NOT ALL INPUTS",
        SandboxKind.Xnor=>"EVEN PARITY",SandboxKind.Toggle=>"LATCHES ON RISING EDGE",
        SandboxKind.Timer=>"BLINKS TWICE A SECOND",
        SandboxKind.Piston=>"PUSHES BLOCKS AHEAD",
        SandboxKind.PressurePlate=>"CUBE MAKES OUTPUT",SandboxKind.ProximitySensor=>"PLAYER MAKES OUTPUT",
        SandboxKind.Delay=>"WAITS BEFORE OUTPUT",SandboxKind.Counter=>"EVERY THIRD PULSE",
        SandboxKind.Randomizer=>"RANDOM OUTPUT EACH SEC",
        SandboxKind.Hinge=>"POWERED ROTARY MOUNT",SandboxKind.Wheel=>"POWERED DRIVE WHEEL",
        SandboxKind.Rope=>"POWERED WINCH PULL",SandboxKind.Conveyor=>"MOVES BLOCKS ON TOP",
        SandboxKind.SensorLight=>"PLAYER DETECTED LIGHT",SandboxKind.DisplayPanel=>"SHOWS WIRE STATE",SandboxKind.Pallet=>"WOODEN GRABBABLE PLATFORM",SandboxKind.Slab=>"SMALL WOODEN PLATFORM",
        _=>"POWERED LIGHT"
    };
    internal void SelectSandbox(int step)
    {
        int selected=Math.Clamp(SandboxSelected+step,0,SandboxItemCount-1);
        if(selected==SandboxSelected) return;
        SandboxSelected=selected;
        EnsureSandboxVisible();
        Dirty=true;InteractionSound?.Invoke();
    }
    void EnsureSandboxVisible()
    {
        if(SandboxSelected<SandboxScroll) SandboxScroll=SandboxSelected;
        else if(SandboxSelected>=SandboxScroll+SandboxRows) SandboxScroll=SandboxSelected-SandboxRows+1;
        SandboxScroll=Math.Clamp(SandboxScroll,0,SandboxMaxScroll);
    }
    internal void ScrollSandbox(int direction)
    {
        if(direction==0) return;
        int scroll=Math.Clamp(SandboxScroll+direction,0,SandboxMaxScroll);
        if(scroll==SandboxScroll) return;
        SandboxScroll=scroll;
        if(SandboxSelected<SandboxScroll) SandboxSelected=SandboxScroll;
        else if(SandboxSelected>=SandboxScroll+SandboxRows) SandboxSelected=SandboxScroll+SandboxRows-1;
        Dirty=true;InteractionSound?.Invoke();
    }
    internal void CycleSandboxCategory(int step)
    {
        int category=Math.Clamp(SandboxCategory+step,0,SandboxItems.Length-1);
        if(category!=SandboxCategory) { SandboxCategory=category;SandboxSelected=0;SandboxScroll=0;Dirty=true;InteractionSound?.Invoke(); }
    }
    internal void TogglePonder()
    {
        FinishEditing();
        if(Open&&PonderPage) { Open=false;PonderPage=false; }
        else { Open=true;PonderPage=true;SandboxPage=false;TitleScreen=false; }
        Dirty=true;InteractionSound?.Invoke();
    }
    internal void MovePonder(int direction)
    { PonderStep=(PonderStep+direction+4)%4;Dirty=true;InteractionSound?.Invoke(); }
    internal void ToggleSandbox()
    {
        FinishEditing();
        if(Open && SandboxPage) { Open=false;SandboxPage=false; }
        else { Open=true;SandboxPage=true;TitleScreen=false; }
        SandboxCategory=0;SandboxSelected=0;SandboxScroll=0;Dirty=true;InteractionSound?.Invoke();
    }
    internal int TitlePage, TitleSelected;
    internal int RenderMode = 2, ProbeQuality = 2, ProbeBounces = 4, HybridDirectSamples = 4;
    internal string RenderModeName => RenderMode == 1 ? "Realtime GI" : "Path traced fixed";
    internal int Temperature, PaletteSteps = 16, Samples = 32, Bounces = 2, ResolutionIndex, FpsLimit;
    internal int TonemapMode = 2;
    internal float Exposure = 2f, Saturation = 1f, Contrast = 1f;
    internal float BrightenSpeed = 1.25f, DarkenSpeed = 2.2f, BloomStrength = 0.1f, BloomThreshold = 0.85f, BloomRadius = 1.9f;
    internal float RedMix = 1f, GreenMix = 1f, BlueMix = 1f;
    internal float BounceStrength = 1.75f, AO = 0.9f, LightIntensity = 1f, LightDensity = 0.65f, FogDensity = 0.008f;
    internal int FogSteps = 8;
    internal float WalkSpeed = 3.2f, SprintMultiplier = 1.75f, CrouchMultiplier = 0.48f;
    internal float Acceleration = 6f, Deceleration = 9f, StanceSmooth = 5f, SprintSmooth = 5f;
    internal float CrouchHeight = 1.12f, StepLength = 2.2f;
    internal float MouseSensitivity = 0.00155f, IdleShake = 3f, WalkShake = 5f;
    internal float SprintShake = 7f, CrouchShake = 2f, HeadBob = 1f;
    internal float SwayAmount = 1.2f, StrafeRoll = 5f, TurnRoll = 1.5f, MovePitch = 4f, SwaySmooth = 11f, CameraSmooth = 11f;
    internal int Page, Selected;
    internal int FpsValue, DebugSeed;
    internal string[] DebugLines = [];
    internal bool MonsterDebug;
    internal bool ShowDebugOverlay;
    internal bool ShowObjectFrames = true;
    internal bool MonsterControlsWritable = true;
    internal bool FlickerControlsWritable = true;
    internal bool MovementControlsWritable = true;
    internal readonly MonsterTuning MonsterSettings = new();
    internal readonly FlickerTuning FlickerSettings = new();
    internal string PlayerName = "BEAN";
    internal float PlayerRed = 0.78f, PlayerGreen = 0.76f, PlayerBlue = 0.70f;
    internal int PlayerColorIndex = -1;
    internal bool EditingName, EditingServer, EditingJoinKey, NetworkConnected, NetworkRemote;
    internal bool EditingText => EditingName || EditingServer || EditingJoinKey;
    internal string ServerAddress = "beanrooms-game.onrender.com", JoinKey = "", NetworkStatus = "OFFLINE";
    internal int NetworkId, NetworkHost, NetworkPlayers;
    internal (PointF A,PointF B,Color Ink)[] MonsterSegments = [];
    internal (PointF At,string Label,Color Ink)[] MonsterPoints = [];
    internal (PointF At,string Label,Color Ink)[] PlayerTags = [];
    internal (PointF A,PointF B,Color Ink)[] SandboxWires = [];
    internal RectangleF[] ObjectFrames = [];
    internal string MonsterTargetText = "TARGET NONE";
    internal int BodyCount;
    internal Action<int>? ObjectAction;
    internal Action? ConnectAction, DisconnectAction;
    internal Action<int>? TitleAction;
    internal Action<int>? SessionAction;
    internal Action? InteractionSound, HoverSound;
    internal float PickupRange=5f, HoldDistance=2.25f, SpringFrequency=8.5f, SpringDamping=1.35f, PidSmoothing=18f, SlideResistance=4.5f;
    internal bool SnapHold;
    internal float SnapStep=.25f;
    internal int CubeColorIndex = 6, CubeMaterialIndex=1;
    static readonly PhysicsMaterial[] CubeMaterials=[PhysicsMaterial.Concrete,PhysicsMaterial.Wood,PhysicsMaterial.Metal,PhysicsMaterial.Plastic];
    static readonly (string Name, Vector3 Color)[] CubeColors =
    {
        ("AMBER", new Vector3(0.82f,0.69f,0.22f)),
        ("RED", new Vector3(0.95f,0.18f,0.12f)),
        ("GREEN", new Vector3(0.16f,0.82f,0.26f)),
        ("BLUE", new Vector3(0.18f,0.40f,1f)),
        ("PURPLE", new Vector3(0.70f,0.22f,0.95f)),
        ("CYAN", new Vector3(0.12f,0.85f,0.92f)),
        ("WHITE", new Vector3(0.92f,0.92f,0.92f)),
        ("ORANGE", new Vector3(1f,0.36f,0.08f))
    };
    internal Vector3 CubeColor => CubeColors[CubeColorIndex].Color;
    internal PhysicsMaterial CubeMaterial => CubeMaterials[CubeMaterialIndex];
    internal void CycleCubeColor(int direction)
    { CubeColorIndex=(CubeColorIndex+direction+CubeColors.Length)%CubeColors.Length;Dirty=true;InteractionSound?.Invoke(); }
    internal void CycleCubeMaterial(int direction)
    { CubeMaterialIndex=(CubeMaterialIndex+direction+CubeMaterials.Length)%CubeMaterials.Length;Dirty=true;InteractionSound?.Invoke(); }
    int dragging = -1;
    int hoveredItem = -1;
    static readonly int[] SampleChoices = { 16, 32, 64, 128, 256 };
    static readonly (string Name,int Page)[] Tabs =
    {
        ("SESSION",9), ("IMAGE",0), ("POST FX",7), ("LIGHTS",1),
        ("FLICKER",8), ("CAMERA",3), ("MOVEMENT",2), ("PHYSICS",4),
        ("MONSTER",5), ("NETWORK",6)
    };
    static readonly Dictionary<char,int[]> Glyphs = new()
    {
        ['A']=[14,17,17,31,17,17,17], ['B']=[30,17,17,30,17,17,30],
        ['C']=[15,16,16,16,16,16,15], ['D']=[30,17,17,17,17,17,30],
        ['E']=[31,16,16,30,16,16,31], ['F']=[31,16,16,30,16,16,16],
        ['G']=[15,16,16,23,17,17,15], ['H']=[17,17,17,31,17,17,17],
        ['I']=[14,4,4,4,4,4,14], ['J']=[7,2,2,2,18,18,12],
        ['K']=[17,18,20,24,20,18,17], ['L']=[16,16,16,16,16,16,31],
        ['M']=[17,27,21,21,17,17,17], ['N']=[17,25,21,19,17,17,17],
        ['O']=[14,17,17,17,17,17,14], ['P']=[30,17,17,30,16,16,16],
        ['Q']=[14,17,17,17,21,18,13], ['R']=[30,17,17,30,20,18,17],
        ['S']=[15,16,16,14,1,1,30], ['T']=[31,4,4,4,4,4,4],
        ['U']=[17,17,17,17,17,17,14], ['V']=[17,17,17,17,17,10,4],
        ['W']=[17,17,17,21,21,21,10], ['X']=[17,17,10,4,10,17,17],
        ['Y']=[17,17,10,4,4,4,4], ['Z']=[31,1,2,4,8,16,31],
        ['0']=[14,17,19,21,25,17,14], ['1']=[4,12,4,4,4,4,14],
        ['2']=[14,17,1,2,4,8,31], ['3']=[30,1,1,14,1,1,30],
        ['4']=[2,6,10,18,31,2,2], ['5']=[31,16,16,30,1,1,30],
        ['6']=[14,16,16,30,17,17,14], ['7']=[31,1,2,4,8,8,8],
        ['8']=[14,17,17,14,17,17,14], ['9']=[14,17,17,15,1,1,14],
        [' '] = [0,0,0,0,0,0,0], ['/']=[1,2,2,4,8,8,16],
        ['|']=[4,4,4,4,4,4,4], ['^']=[4,10,17,0,0,0,0],
        ['*']=[0,4,21,14,21,4,0],
        ['<']=[2,4,8,16,8,4,2], ['>']=[8,4,2,1,2,4,8],
        ['.']=[0,0,0,0,0,12,12], ['-']=[0,0,0,31,0,0,0],
        ['+']=[0,4,4,31,4,4,0], [':']=[0,4,4,0,4,4,0]
    };
    static readonly string[][] Labels =
    {
        new[] { "DITHER", "PALETTE REDUCE", "PALETTE COLORS", "SATURATION", "TEMPERATURE", "CONTRAST", "RED MIX", "GREEN MIX", "BLUE MIX" },
        new[] { "RENDER MODE", "PATH SAMPLES", "PATH BOUNCES", "BOUNCE LIGHT", "AO STRENGTH", "LIGHT INTENSITY", "LIGHT DENSITY", "RESOLUTION", "FIXTURES", "FPS LIMIT", "VSYNC", "VOLUMETRICS", "FOG DENSITY", "FOG STEPS", "PROBE GRID", "PROBE BOUNCES", "DIRECT SAMPLES" },
        new[] { "WALK SPEED", "SPRINT X", "CROUCH X", "ACCELERATION", "DECELERATION", "STANCE SMOOTH", "SPRINT SMOOTH", "CROUCH HEIGHT", "STEP LENGTH" },
        new[] { "MOUSE SENS", "IDLE SHAKE", "WALK SHAKE", "SPRINT SHAKE", "CROUCH SHAKE", "HEAD BOB", "SWAY AMOUNT", "STRAFE ROLL", "TURN ROLL", "MOVE PITCH", "LOOK SMOOTH" },
        new[] { "PICKUP RANGE", "HOLD DIST", "SPRING FREQ", "SPRING DAMP", "HOLD SMOOTH", "SLIDE RESIST", "GRID SNAP", "SNAP STEP", "CUBE COUNT", "DEBUG UI", "OBJECT FRAMES" },
        new[] { "MONSTER", "ROAM SPEED", "SEARCH SPEED", "CHASE SPEED", "CHASE TURN", "SEARCH TURN", "ROAM TURN", "SIGHT RANGE", "SIGHT ANGLE", "SEARCH RADIUS", "ROAM RADIUS" },
        new[] { "PLAYER NAME", "BEAN COLOR", "RED", "GREEN", "BLUE", "PHOTON REGION", "JOIN KEY", "CONNECT", "DISCONNECT", "CONNECTION", "PLAYER ID", "PLAYERS", "ROLE" },
        new[] { "POST FX", "TONEMAP", "EXPOSURE", "AUTO EXPOSURE", "BRIGHTEN SPEED", "DARKEN SPEED", "BLOOM", "BLOOM STRENGTH", "BLOOM THRESHOLD", "BLOOM RADIUS" },
        new[] { "MINOR FLICKER", "MINOR INTERVAL", "MINOR CHANCE", "MINOR DURATION", "RANDOM EVENTS", "EVENT INTERVAL", "EVENT LENGTH", "EVENT SPEED MIN", "EVENT SPEED MAX", "EVENT OFF CHANCE", "CHASE FLICKER", "CHASE FOG", "CHASE FOG MAX", "FOG RISE", "FOG FALL", "CHASE RED", "CHASE RED CHANCE", "BLACKOUT CHANCE" },
        new[] { "RESUME", "LEAVE MULTIPLAYER", "MAIN MENU", "QUIT GAME", "CONNECTION", "PLAYERS", "ROLE", "SANDBOX MODE" }
    };

    internal void Toggle()
    {
        if(PonderPage) { TogglePonder();return; }
        if(SandboxPage) { ToggleSandbox();return; }
        FinishEditing();
        if(Open && SettingsFromTitle && !TitleScreen) ShowTitle();
        else if(Open && TitleScreen && TitlePage==1) ShowTitle();
        else { Open=!Open;TitleScreen=false;SettingsFromTitle=false; }
        dragging=-1;hoveredItem=-1;Dirty=true;InteractionSound?.Invoke();
    }
    internal void ShowTitle()
    {
        FinishEditing(); Open=true;TitleScreen=true;SettingsFromTitle=false;
        TitlePage=0;TitleSelected=0;hoveredItem=-1;Dirty=true;
    }
    internal void LeaveTitle()
    {
        FinishEditing(); Open=false;TitleScreen=false;SettingsFromTitle=false;Dirty=true;
    }
    internal void ShowJoinScreen()
    {
        FinishEditing();TitleScreen=true;TitlePage=1;TitleSelected=0;Dirty=true;
    }
    internal void ShowSettingsFromTitle()
    {
        FinishEditing();TitleScreen=false;SettingsFromTitle=true;SetPage(0);
    }
    internal void SetPage(int page)
    {
        if(page==2 && !MovementControlsWritable) return;
        FinishEditing(); Page=Math.Clamp(page,0,9); Selected=0;
        dragging=-1;hoveredItem=-1;Dirty=true;InteractionSound?.Invoke();
    }
    internal void CyclePage(int step)
    {
        int index=Array.FindIndex(Tabs,tab=>tab.Page==Page);
        for(int offset=1;offset<=Tabs.Length;offset++)
        {
            int page=Tabs[(index+step*offset+Tabs.Length*2)%Tabs.Length].Page;
            if(page!=2 || MovementControlsWritable) { SetPage(page);return; }
        }
    }
    internal void ResetDefaults()
    {
        var defaults = new DebugMenu();
        Dithering=defaults.Dithering; PaletteReduction=defaults.PaletteReduction;
        Temperature=defaults.Temperature; PaletteSteps=defaults.PaletteSteps;
        Saturation=defaults.Saturation; Contrast=defaults.Contrast;
        RedMix=defaults.RedMix; GreenMix=defaults.GreenMix; BlueMix=defaults.BlueMix;
        PostEnabled=defaults.PostEnabled; TonemapMode=defaults.TonemapMode;
        Exposure=defaults.Exposure; AutoExposure=defaults.AutoExposure;
        BrightenSpeed=defaults.BrightenSpeed; DarkenSpeed=defaults.DarkenSpeed;
        Bloom=defaults.Bloom; BloomStrength=defaults.BloomStrength;
        BloomThreshold=defaults.BloomThreshold; BloomRadius=defaults.BloomRadius;
        RenderMode=defaults.RenderMode; ProbeQuality=defaults.ProbeQuality;
        ProbeBounces=defaults.ProbeBounces; HybridDirectSamples=defaults.HybridDirectSamples;
        Samples=defaults.Samples; Bounces=defaults.Bounces;
        BounceStrength=defaults.BounceStrength; AO=defaults.AO;
        LightIntensity=defaults.LightIntensity; LightDensity=defaults.LightDensity;
        Lights=defaults.Lights; Volumetrics=defaults.Volumetrics;
        FogDensity=defaults.FogDensity; FogSteps=defaults.FogSteps;
        ResolutionIndex=defaults.ResolutionIndex; FpsLimit=defaults.FpsLimit; Vsync=defaults.Vsync;
        if(MovementControlsWritable)
        {
            ResetMovementDefaults();
        }
        MouseSensitivity=defaults.MouseSensitivity;
        IdleShake=defaults.IdleShake; WalkShake=defaults.WalkShake;
        SprintShake=defaults.SprintShake; CrouchShake=defaults.CrouchShake;
        HeadBob=defaults.HeadBob; SwayAmount=defaults.SwayAmount;
        StrafeRoll=defaults.StrafeRoll; TurnRoll=defaults.TurnRoll;
        MovePitch=defaults.MovePitch; SwaySmooth=defaults.SwaySmooth;
        CameraSmooth=defaults.CameraSmooth;
        PickupRange=defaults.PickupRange; HoldDistance=defaults.HoldDistance;
        SpringFrequency=defaults.SpringFrequency; SpringDamping=defaults.SpringDamping;
        PidSmoothing=defaults.PidSmoothing; SlideResistance=defaults.SlideResistance;
        SnapHold=defaults.SnapHold;SnapStep=defaults.SnapStep;
        CubeColorIndex=defaults.CubeColorIndex;CubeMaterialIndex=defaults.CubeMaterialIndex;
        ShowDebugOverlay=defaults.ShowDebugOverlay; ShowObjectFrames=defaults.ShowObjectFrames;
        MonsterDebug=defaults.MonsterDebug;
        if(MonsterControlsWritable) MonsterSettings.CopyFrom(defaults.MonsterSettings);
        if(FlickerControlsWritable) FlickerSettings.CopyFrom(defaults.FlickerSettings);
        PlayerName=defaults.PlayerName; PlayerRed=defaults.PlayerRed;
        PlayerGreen=defaults.PlayerGreen; PlayerBlue=defaults.PlayerBlue;
        PlayerColorIndex=defaults.PlayerColorIndex; FinishEditing();
        dragging=-1; Dirty=true;
    }
    internal void ResetMovementDefaults()
    {
        WalkSpeed=3.2f;SprintMultiplier=1.75f;CrouchMultiplier=0.48f;
        Acceleration=6f;Deceleration=9f;StanceSmooth=5f;
        SprintSmooth=5f;CrouchHeight=1.12f;StepLength=2.2f;
        Dirty=true;
    }
    internal void EditNameCharacter(char c)
    {
        c=char.ToUpperInvariant(c);
        if(EditingName && (char.IsAsciiLetterOrDigit(c)||c==' '||c=='-') && PlayerName.Length<12)
        { PlayerName+=c; Dirty=true; InteractionSound?.Invoke(); }
    }
    internal void BackspaceName() { if(EditingName && PlayerName.Length>0) { PlayerName=PlayerName[..^1];Dirty=true;InteractionSound?.Invoke(); } }
    internal void FinishName() { EditingName=false;PlayerName=PlayerName.Trim();if(PlayerName.Length==0) PlayerName="BEAN";Dirty=true;InteractionSound?.Invoke(); }
    internal void FinishEditing()
    {
        if(EditingName) FinishName();
        EditingServer=false; EditingJoinKey=false;
        ServerAddress=ServerAddress.Trim(); JoinKey=JoinKey.Trim();
        Dirty=true;
    }
    internal void EditConnectionCharacter(char c)
    {
        if(c<32 || c>126) return;
        if(EditingServer && ServerAddress.Length<100) ServerAddress+=c;
        else if(EditingJoinKey && JoinKey.Length<128) JoinKey+=c;
        else return;
        Dirty=true;
    }
    internal void PasteConnectionText(string text)
    {
        foreach(char c in text) EditConnectionCharacter(c);
    }
    internal void BackspaceConnection()
    {
        if(EditingServer && ServerAddress.Length>0) ServerAddress=ServerAddress[..^1];
        else if(EditingJoinKey && JoinKey.Length>0) JoinKey=JoinKey[..^1];
        Dirty=true;
    }
    internal void ClearConnectionField()
    {
        if(EditingServer) ServerAddress="";
        else if(EditingJoinKey) JoinKey="";
        Dirty=true;
    }
    internal void Select(int step)
    {
        if(TitleScreen)
        {
            int next=Math.Clamp(TitleSelected+step,0,TitlePage==0?3:2);
            if(next!=TitleSelected) InteractionSound?.Invoke();
            TitleSelected=next;Dirty=true;return;
        }
        int selected=Math.Clamp(Selected + step, 0, Labels[Page].Length - 1);
        if(selected!=Selected) InteractionSound?.Invoke(); Selected=selected; Dirty=true;
    }
    internal void AdjustSelected(int direction)
    {
        if(TitleScreen) ActivateTitleSelection();
        else Adjust(Selected,direction);
    }
    void ActivateTitleSelection()
    {
        InteractionSound?.Invoke();
        if(TitlePage==0)
        {
            switch(TitleSelected)
            {
                case 1: ShowJoinScreen();break;
                case 2: ShowSettingsFromTitle();break;
                default: TitleAction?.Invoke(TitleSelected);break;
            }
        }
        else switch(TitleSelected)
        {
            case 0: FinishEditing();EditingJoinKey=true;JoinKey="";break;
            case 1: FinishEditing();ConnectAction?.Invoke();break;
            case 2: ShowTitle();break;
        }
        Dirty=true;
    }

    internal void MouseDown(int x, int y)
    {
        if(SandboxPage)
        {
            if(y<14 && x>=307) { ToggleSandbox();return; }
            if(y>=148 && y<164) { CycleCubeColor(x<155?-1:1);return; }
            if(y>=168 && y<184) { CycleCubeMaterial(x<155?-1:1);return; }
            if(y>=186 && y<202) { ObjectAction?.Invoke(2);Dirty=true;InteractionSound?.Invoke();return; }
            if(y>=17 && y<29 && x>=12)
            {
                int category=(x-12)/75;
                if(category>=0 && category<SandboxItems.Length && (x-12)%75<70)
                { SandboxCategory=category;SandboxSelected=0;Dirty=true;InteractionSound?.Invoke();return; }
            }
            if(x>=15 && x<305 && y>=SandboxTop && y<SandboxTop+SandboxRows*SandboxPitch)
            {
                int slot=(y-SandboxTop)/SandboxPitch;
                int itemRow=SandboxScroll+slot;
                if(slot>=0 && slot<SandboxRows && itemRow>=0 && itemRow<SandboxItemCount)
                {
                    SandboxSelected=itemRow;
                    SandboxKind kind=SandboxItems[SandboxCategory][SandboxSelected];
                    if(SandboxEnabled || kind==SandboxKind.Cube) SandboxSpawn?.Invoke(kind);
                    Dirty=true;InteractionSound?.Invoke();
                }
                return;
            }
            return;
        }
        if(TitleScreen)
        {
            int titleRow=TitleRowAt(x,y);
            if(titleRow>=0) { TitleSelected=titleRow;ActivateTitleSelection(); }
            return;
        }
        if (y>=2 && y<14)
        {
            if(x>=307) { Toggle(); return; }
            if(x>=270 && x<307) { ResetDefaults(); InteractionSound?.Invoke(); return; }
        }
        int tab=TabAt(x,y);
        if(tab>=0)
        {
            SetPage(Tabs[tab].Page);
            return;
        }
        int row = (y - 54) / RowPitch;
        if (y < 54 || row < 0 || row >= Labels[Page].Length || x < 15 || x >= 305) return;
        Selected = row;
        if (IsSlider(row) && x >= 155 && x <= 254)
        {
            dragging = row;
            SetSliderFromX(row,x);
        }
        else if(IsChoice(row) && x>=316-(Value(row).Length+4)*6 &&
                x<316-(Value(row).Length+1)*6) Adjust(row,-1);
        else Adjust(row,1);
        Dirty = true;
    }
    internal void MouseMove(int x,int y)
    {
        if(SandboxPage) return;
        if(TitleScreen)
        {
            int row=TitleRowAt(x,y);
            if(row>=0 && row!=TitleSelected)
            { TitleSelected=row;Dirty=true;HoverSound?.Invoke(); }
            return;
        }
        if (dragging < 0)
        {
            int hovered=-1;
            if(y>=2 && y<14 && x>=270 && x<307) hovered=109;
            else if(y>=2 && y<14 && x>=307) hovered=110;
            else if(TabAt(x,y) is int tab && tab>=0) hovered=100+tab;
            else if(x>=15 && x<305 && y>=54)
            {
                int row=(y-54)/RowPitch;
                if(row>=0 && row<Labels[Page].Length) hovered=row;
            }
            if(hovered!=hoveredItem)
            {
                hoveredItem=hovered;
                if(hovered>=0)
                {
                    if(hovered<100) Selected=hovered;
                    Dirty=true;
                    HoverSound?.Invoke();
                }
            }
            return;
        }
        SetSliderFromX(dragging,x);
    }
    internal void MouseUp() => dragging = -1;
    internal void MouseWheel(int delta)
    {
        if(SandboxPage) { ScrollSandbox(-Math.Sign(delta));return; }
    }
    int TitleRowAt(int x,int y)
    {
        if(x<15 || x>=305) return -1;
        if(TitlePage==0)
        {
            int row=(y-69)/24;
            return row>=0 && row<4 && y<69+row*24+16 ? row : -1;
        }
        if(y>=68 && y<97) return 0;
        if(y>=108 && y<137) return 1;
        if(y>=155 && y<169) return 2;
        if(y>=178 && y<192) return 3;
        return -1;
    }
    int RowPitch => Page is 1 or 8 ? 10 : Page is 4 or 6 ? 12 : 14;

    static int TabAt(int x,int y)
    {
        if(x<12 || x>=312 || y<17 || y>=51) return -1;
        int column=(x-12)/75;
        int row=(y-17)/12;
        if((x-12)%75>=70 || (y-17)%12>=10) return -1;
        int index=row*4+column;
        return index<Tabs.Length?index:-1;
    }

    bool IsSlider(int row) => Page switch
    {
        0 => row is 2 or 3 or 4 or 5 or 6 or 7 or 8,
        1 => row is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 9 or 12 or 13 or 14 or 15 or 16,
        4 => row is 0 or 1 or 2 or 3 or 4 or 5,
        5 => row > 0,
        6 => row is 2 or 3 or 4,
        7 => row is 2 or 4 or 5 or 7 or 8 or 9,
        8 => row is 1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 12 or 13 or 14 or 16 or 17,
        9 => false,
        _ => true
    };
    bool IsChoice(int row) => Page switch
    {
        1 => row==0,
        6 => row==1,
        7 => row==1,
        _ => false
    };
    void SetSliderFromX(int row,int x)
    {
        float target=Math.Clamp((x-155)/99f,0f,1f);
        float current=Fraction(row);
        int direction=target>current?1:-1;
        bool changed=false;
        for(int attempt=0;attempt<1200;attempt++)
        {
            float distance=MathF.Abs(current-target);
            Adjust(row,direction,false);
            float next=Fraction(row);
            if(MathF.Abs(next-current)<0.000001f) break;
            if(MathF.Abs(next-target)>distance)
            {
                Adjust(row,-direction,false);
                break;
            }
            changed=true;
            current=next;
            if(distance<0.00001f) break;
        }
        if(changed) InteractionSound?.Invoke();
    }
    void Adjust(int row, int direction,bool playSound=true)
    {
        if((Page == 5 && !MonsterControlsWritable) ||
           (Page == 8 && !FlickerControlsWritable) ||
           (Page == 2 && !MovementControlsWritable)) return;
        if (Page == 0)
        {
            switch (row)
            {
                case 0: Dithering = !Dithering; break;
                case 1: PaletteReduction = !PaletteReduction; break;
                case 2: PaletteSteps = Math.Clamp(PaletteSteps + direction, 2, 32); break;
                case 3: Saturation = Math.Clamp(Saturation + direction * 0.01f, 0f, 2f); break;
                case 4: Temperature = Math.Clamp(Temperature + direction, -100, 100); break;
                case 5: Contrast = Math.Clamp(Contrast + direction * 0.01f, 0.5f, 1.5f); break;
                case 6: RedMix = Math.Clamp(RedMix + direction * 0.01f, 0.5f, 1.5f); break;
                case 7: GreenMix = Math.Clamp(GreenMix + direction * 0.01f, 0.5f, 1.5f); break;
                case 8: BlueMix = Math.Clamp(BlueMix + direction * 0.01f, 0.5f, 1.5f); break;
            }
        }
        else if (Page == 1)
        {
            switch (row)
            {
                case 0: RenderMode = RenderMode == 1 ? 2 : 1; break;
                case 1:
                    int sampleIndex = Array.IndexOf(SampleChoices, Samples);
                    Samples = SampleChoices[Math.Clamp(sampleIndex + direction, 0, SampleChoices.Length - 1)];
                    break;
                case 2: Bounces = Math.Clamp(Bounces + direction, 1, 3); break;
                case 3: BounceStrength = Math.Clamp(BounceStrength + direction * 0.01f, 0f, 3f); break;
                case 4: AO = Math.Clamp(AO + direction * 0.01f, 0f, 1.5f); break;
                case 5: LightIntensity = Math.Clamp(LightIntensity + direction * 0.01f, 0.1f, 2f); break;
                case 6: LightDensity = Math.Clamp(LightDensity + direction * 0.01f, 0f, 1f); break;
                case 7: ResolutionIndex = Math.Clamp(ResolutionIndex + direction, 0, 2); break;
                case 8: Lights = !Lights; break;
                case 9: FpsLimit = FpsLimit == 0 ? (direction < 0 ? 240 : 15) : FpsLimit >= 240 && direction > 0 ? 0 : Math.Clamp(FpsLimit + direction,15,240); break;
                case 10: Vsync = !Vsync; break;
                case 11: Volumetrics = !Volumetrics; break;
                case 12: FogDensity = Math.Clamp(FogDensity + direction * 0.001f, 0f, 0.1f); break;
                case 13: FogSteps = Math.Clamp(FogSteps + direction, 2, 8); break;
                case 14: ProbeQuality = Math.Clamp(ProbeQuality + direction, 0, 3); break;
                case 15: ProbeBounces = Math.Clamp(ProbeBounces + direction, 1, 6); break;
                case 16: HybridDirectSamples = Math.Clamp(HybridDirectSamples + direction, 1, 8); break;
            }
        }
        else if (Page == 2)
        {
            switch (row)
            {
                case 0: WalkSpeed = Math.Clamp(WalkSpeed + direction * 0.01f,1f,8f); break;
                case 1: SprintMultiplier = Math.Clamp(SprintMultiplier + direction * 0.01f,1f,3f); break;
                case 2: CrouchMultiplier = Math.Clamp(CrouchMultiplier + direction * 0.01f,0.2f,1f); break;
                case 3: Acceleration = Math.Clamp(Acceleration + direction * 0.1f,1f,15f); break;
                case 4: Deceleration = Math.Clamp(Deceleration + direction * 0.1f,1f,20f); break;
                case 5: StanceSmooth = Math.Clamp(StanceSmooth + direction * 0.1f,1f,12f); break;
                case 6: SprintSmooth = Math.Clamp(SprintSmooth + direction * 0.1f,1f,12f); break;
                case 7: CrouchHeight = Math.Clamp(CrouchHeight + direction * 0.01f,0.8f,1.5f); break;
                case 8: StepLength = Math.Clamp(StepLength + direction * 0.01f,1f,3f); break;
            }
        }
        else if(Page == 4)
        {
            switch(row)
            {
                case 0: PickupRange=Math.Clamp(PickupRange+direction*0.1f,2f,8f); break;
                case 1: HoldDistance=Math.Clamp(HoldDistance+direction*0.1f,0.35f,6f); break;
                case 2: SpringFrequency=Math.Clamp(SpringFrequency+direction*0.1f,2f,18f); break;
                case 3: SpringDamping=Math.Clamp(SpringDamping+direction*0.01f,0.3f,2f); break;
                case 4: PidSmoothing=Math.Clamp(PidSmoothing+direction*0.1f,4f,30f); break;
                case 5: SlideResistance=Math.Clamp(SlideResistance+direction*0.1f,0f,12f); break;
                case 6: SnapHold=!SnapHold;break;
                case 7: SnapStep=Math.Clamp(SnapStep+direction*.05f,.05f,1f);break;
                case 9: ShowDebugOverlay=!ShowDebugOverlay; break;
                case 10: ShowObjectFrames=!ShowObjectFrames; break;
            }
        }
        else if(Page == 5)
        {
            switch(row)
            {
                case 0: MonsterSettings.Enabled=!MonsterSettings.Enabled; break;
                case 1: MonsterSettings.RoamSpeed=Math.Clamp(MonsterSettings.RoamSpeed+direction*0.1f,0.3f,4f); break;
                case 2: MonsterSettings.SearchSpeed=Math.Clamp(MonsterSettings.SearchSpeed+direction*0.1f,0.5f,8f); break;
                case 3: MonsterSettings.ChaseSpeed=Math.Clamp(MonsterSettings.ChaseSpeed+direction*0.1f,2f,14f); break;
                case 4: MonsterSettings.ChaseTurn=Math.Clamp(MonsterSettings.ChaseTurn+direction,20f,240f); break;
                case 5: MonsterSettings.SearchTurn=Math.Clamp(MonsterSettings.SearchTurn+direction,20f,240f); break;
                case 6: MonsterSettings.RoamTurn=Math.Clamp(MonsterSettings.RoamTurn+direction,20f,240f); break;
                case 7: MonsterSettings.SightRange=Math.Clamp(MonsterSettings.SightRange+direction*1f,5f,80f); break;
                case 8: MonsterSettings.SightAngle=Math.Clamp(MonsterSettings.SightAngle+direction,45f,360f); break;
                case 9: MonsterSettings.SearchRadius=Math.Clamp(MonsterSettings.SearchRadius+direction*0.1f,0.5f,5f); break;
                case 10: MonsterSettings.RoamRadius=Math.Clamp(MonsterSettings.RoamRadius+direction*1f,5f,60f); break;
            }
        }
        else if(Page == 6)
        {
            switch(row)
            {
                case 0: FinishEditing();EditingName=true;PlayerName="";break;
                case 1: PlayerColorIndex=(PlayerColorIndex+direction+CubeColors.Length)%CubeColors.Length;
                    Vector3 color=CubeColors[PlayerColorIndex].Color;
                    PlayerRed=color.X;PlayerGreen=color.Y;PlayerBlue=color.Z;break;
                case 2: PlayerRed=Math.Clamp(PlayerRed+direction*0.01f,0.05f,1f);PlayerColorIndex=-1;break;
                case 3: PlayerGreen=Math.Clamp(PlayerGreen+direction*0.01f,0.05f,1f);PlayerColorIndex=-1;break;
                case 4: PlayerBlue=Math.Clamp(PlayerBlue+direction*0.01f,0.05f,1f);PlayerColorIndex=-1;break;
                case 5: break;
                case 6: FinishEditing();EditingJoinKey=true;JoinKey="";break;
                case 7: FinishEditing();ConnectAction?.Invoke();break;
                case 8: FinishEditing();DisconnectAction?.Invoke();break;
            }
        }
        else if(Page == 9)
        {
            // The server validates the sender. Do not use a stale client-side host
            // flag to prevent a host from issuing the request.
            if(row==7 && (!NetworkConnected || SandboxSupported)) SandboxToggle?.Invoke();
            else if(row<4) SessionAction?.Invoke(row);
            else return;
        }
        else if(Page == 7)
        {
            switch(row)
            {
                case 0: PostEnabled=!PostEnabled;break;
                case 1: TonemapMode=(TonemapMode+direction+4)%4;break;
                case 2: Exposure=Math.Clamp(Exposure+direction*0.01f,0.25f,3f);break;
                case 3: AutoExposure=!AutoExposure;break;
                case 4: BrightenSpeed=Math.Clamp(BrightenSpeed+direction*0.01f,0.1f,8f);break;
                case 5: DarkenSpeed=Math.Clamp(DarkenSpeed+direction*0.01f,0.1f,8f);break;
                case 6: Bloom=!Bloom;break;
                case 7: BloomStrength=Math.Clamp(BloomStrength+direction*0.01f,0f,2f);break;
                case 8: BloomThreshold=Math.Clamp(BloomThreshold+direction*0.01f,0.25f,4f);break;
                case 9: BloomRadius=Math.Clamp(BloomRadius+direction*0.01f,0.5f,4f);break;
            }
        }
        else if(Page == 8)
        {
            switch(row)
            {
                case 0: FlickerSettings.MinorEnabled=!FlickerSettings.MinorEnabled;break;
                case 1: FlickerSettings.MinorInterval=Math.Clamp(FlickerSettings.MinorInterval+direction,8f,120f);break;
                case 2: FlickerSettings.MinorChance=Math.Clamp(FlickerSettings.MinorChance+direction*0.01f,0f,1f);break;
                case 3: FlickerSettings.MinorDuration=Math.Clamp(FlickerSettings.MinorDuration+direction*0.1f,0.5f,3f);break;
                case 4: FlickerSettings.RandomEvents=!FlickerSettings.RandomEvents;break;
                case 5: FlickerSettings.EventInterval=Math.Clamp(FlickerSettings.EventInterval+direction,20f,300f);break;
                case 6: FlickerSettings.EventDuration=Math.Clamp(FlickerSettings.EventDuration+direction*0.1f,1f,12f);break;
                case 7: FlickerSettings.EventSpeedMin=Math.Clamp(FlickerSettings.EventSpeedMin+direction*0.1f,2f,FlickerSettings.EventSpeedMax);break;
                case 8: FlickerSettings.EventSpeedMax=Math.Clamp(FlickerSettings.EventSpeedMax+direction*0.1f,FlickerSettings.EventSpeedMin,25f);break;
                case 9: FlickerSettings.EventOffChance=Math.Clamp(FlickerSettings.EventOffChance+direction*0.01f,0.05f,0.9f);break;
                case 10: FlickerSettings.ChaseFlicker=!FlickerSettings.ChaseFlicker;break;
                case 11: FlickerSettings.ChaseFog=!FlickerSettings.ChaseFog;break;
                case 12: FlickerSettings.ChaseFogMax=Math.Clamp(FlickerSettings.ChaseFogMax+direction*0.001f,0f,0.1f);break;
                case 13: FlickerSettings.FogRise=Math.Clamp(FlickerSettings.FogRise+direction*0.1f,0.2f,8f);break;
                case 14: FlickerSettings.FogFall=Math.Clamp(FlickerSettings.FogFall+direction*0.1f,0.2f,8f);break;
                case 15: FlickerSettings.ChaseRed=!FlickerSettings.ChaseRed;break;
                case 16: FlickerSettings.ChaseRedChance=Math.Clamp(FlickerSettings.ChaseRedChance+direction*0.01f,0.08f,1f);break;
                case 17: FlickerSettings.BlackoutChance=Math.Clamp(FlickerSettings.BlackoutChance+direction*0.01f,0f,1f);break;
            }
        }
        else
        {
            switch (row)
            {
                case 0: MouseSensitivity = Math.Clamp(MouseSensitivity + direction * 0.00001f,0.0004f,0.005f); break;
                case 1: IdleShake = Math.Clamp(IdleShake + direction * 0.01f,0f,4f); break;
                case 2: WalkShake = Math.Clamp(WalkShake + direction * 0.01f,0f,8f); break;
                case 3: SprintShake = Math.Clamp(SprintShake + direction * 0.01f,0f,8f); break;
                case 4: CrouchShake = Math.Clamp(CrouchShake + direction * 0.01f,0f,4f); break;
                case 5: HeadBob = Math.Clamp(HeadBob + direction * 0.01f,0f,2f); break;
                case 6: SwayAmount = Math.Clamp(SwayAmount + direction * 0.01f,0f,2f); break;
                case 7: StrafeRoll = Math.Clamp(StrafeRoll + direction * 0.1f,0f,5f); break;
                case 8: TurnRoll = Math.Clamp(TurnRoll + direction * 0.1f,0f,3f); break;
                case 9: MovePitch = Math.Clamp(MovePitch + direction * 0.1f,0f,4f); break;
                case 10: SwaySmooth = CameraSmooth = Math.Clamp(CameraSmooth + direction * 0.1f,1f,20f); break;
            }
        }
        Dirty = true;
        if(playSound) InteractionSound?.Invoke();
    }

    string Value(int row)
    {
        if(Page == 9) return row switch
        {
            0 => "PLAY", 1 => NetworkConnected || NetworkRemote ? "LEAVE" : "OFFLINE",
            2 => "OPEN", 3 => "EXIT",
            4 => NetworkConnected ? (NetworkRemote ? "ONLINE" : "LOCAL") : NetworkStatus,
            5 => NetworkConnected ? NetworkPlayers.ToString() : "1",
            6 => NetworkConnected ? (NetworkId==NetworkHost ? "HOST" : "GUEST") : "SOLO",
            7 => NetworkConnected&&!SandboxSupported ? "SERVER UPDATE REQUIRED" : SandboxEnabled ? "ON" : "OFF",
            _ => ""
        };
        if(Page == 8) return row switch
        {
            0 => FlickerSettings.MinorEnabled?"ON":"OFF",
            1 => $"{FlickerSettings.MinorInterval:0}S",
            2 => $"{FlickerSettings.MinorChance*100f:0}%",
            3 => $"{FlickerSettings.MinorDuration:0.0}S",
            4 => FlickerSettings.RandomEvents?"ON":"OFF",
            5 => $"{FlickerSettings.EventInterval:0}S",
            6 => $"{FlickerSettings.EventDuration:0.0}S",
            7 => $"{FlickerSettings.EventSpeedMin:0.0}",
            8 => $"{FlickerSettings.EventSpeedMax:0.0}",
            9 => $"{FlickerSettings.EventOffChance*100f:0}%",
            10 => FlickerSettings.ChaseFlicker?"ON":"OFF",
            11 => FlickerSettings.ChaseFog?"ON":"OFF",
            12 => $"{FlickerSettings.ChaseFogMax:0.000}",
            13 => $"{FlickerSettings.FogRise:0.0}",
            14 => $"{FlickerSettings.FogFall:0.0}",
            15 => FlickerSettings.ChaseRed?"ON":"OFF",
            16 => $"{FlickerSettings.ChaseRedChance*100f:0}%",
            17 => $"{FlickerSettings.BlackoutChance*100f:0}%",
            _ => ""
        };
        if(Page == 7) return row switch
        {
            0 => PostEnabled?"ON":"OFF",
            1 => TonemapMode switch { 0=>"OFF",1=>"FILMIC",2=>"ACES",_=>"NEUTRAL" },
            2 => $"{Exposure:0.00}",3 => AutoExposure?"ON":"OFF",
            4 => $"{BrightenSpeed:0.00}",5 => $"{DarkenSpeed:0.00}",
            6 => Bloom?"ON":"OFF",7 => $"{BloomStrength:0.00}",
            8 => $"{BloomThreshold:0.00}",9 => $"{BloomRadius:0.00}",_ => ""
        };
        if(Page == 6) return row switch
        {
            0 => EditingName ? PlayerName+"_" : PlayerName,
            1 => PlayerColorIndex<0 ? "CUSTOM" : CubeColors[PlayerColorIndex].Name,
            2 => $"{PlayerRed:0.00}",3 => $"{PlayerGreen:0.00}",4 => $"{PlayerBlue:0.00}",
            5 => "EU",
            6 => JoinKey.Length==0 ? (EditingJoinKey?"-":"ENTER KEY") : new string('*',Math.Min(JoinKey.Length,16))+(EditingJoinKey?"-":""),
            7 => "JOIN", 8 => "LEAVE",
            9 => NetworkConnected ? (NetworkRemote ? "ONLINE" : "LOCAL") : NetworkStatus,
            10 => NetworkConnected ? NetworkId.ToString() : "-",
            11 => NetworkConnected ? NetworkPlayers.ToString() : "1",
            12 => NetworkConnected ? (NetworkId==NetworkHost ? "HOST" : "GUEST") : "SOLO",
            _ => ""
        };
        if(Page == 5) return row switch
        {
            0 => MonsterSettings.Enabled ? "ON" : "OFF",
            1 => $"{MonsterSettings.RoamSpeed:0.0}",
            2 => $"{MonsterSettings.SearchSpeed:0.0}",
            3 => $"{MonsterSettings.ChaseSpeed:0.0}",
            4 => $"{MonsterSettings.ChaseTurn:0}",
            5 => $"{MonsterSettings.SearchTurn:0}",
            6 => $"{MonsterSettings.RoamTurn:0}",
            7 => $"{MonsterSettings.SightRange:0}",
            8 => $"{MonsterSettings.SightAngle:0}",
            9 => $"{MonsterSettings.SearchRadius:0.0}",
            10 => $"{MonsterSettings.RoamRadius:0}",
            _ => ""
        };
        if(Page == 4) return row switch
        {
            0 => $"{PickupRange:0.0}",1 => $"{HoldDistance:0.0}",
            2 => $"{SpringFrequency:0.0}",3 => $"{SpringDamping:0.00}",
            4 => $"{PidSmoothing:0.0}",5 => $"{SlideResistance:0.0}",
            6 => SnapHold?"ON":"OFF",7 => $"{SnapStep:0.00}",8 => $"{BodyCount}",
            9 => ShowDebugOverlay ? "ON" : "OFF",10 => ShowObjectFrames ? "ON" : "OFF",_ => ""
        };
        if (Page == 0) return row switch
        {
            0 => Dithering ? "ON" : "OFF",
            1 => PaletteReduction ? "ON" : "OFF",
            2 => $"{PaletteSteps}^3",
            3 => $"{Saturation:0.00}",
            4 => $"{Temperature:+#;-#;0}",
            5 => $"{Contrast:0.00}",
            6 => $"{RedMix:0.00}",
            7 => $"{GreenMix:0.00}",
            8 => $"{BlueMix:0.00}",
            _ => ""
        };
        if (Page == 2) return row switch
        {
            0 => $"{WalkSpeed:0.00}",
            1 => $"{SprintMultiplier:0.00}X",
            2 => $"{CrouchMultiplier:0.00}X",
            3 => $"{Acceleration:0.0}",
            4 => $"{Deceleration:0.0}",
            5 => $"{StanceSmooth:0.0}",
            6 => $"{SprintSmooth:0.0}",
            7 => $"{CrouchHeight:0.00}",
            8 => $"{StepLength:0.00}",
            _ => ""
        };
        if (Page == 3) return row switch
        {
            0 => $"{MouseSensitivity*1000f:0.00}",
            1 => $"{IdleShake:0.00}",
            2 => $"{WalkShake:0.00}",
            3 => $"{SprintShake:0.00}",
            4 => $"{CrouchShake:0.00}",
            5 => $"{HeadBob:0.00}",
            6 => $"{SwayAmount:0.00}",
            7 => $"{StrafeRoll:0.0}",
            8 => $"{TurnRoll:0.0}",
            9 => $"{MovePitch:0.0}",
            10 => $"{CameraSmooth:0.0}",
            _ => ""
        };
        return row switch
        {
            0 => RenderMode == 1 ? "HYBRID GI" : "PATH TRACED FIXED",
            1 => $"{Samples} SPP",
            2 => Bounces.ToString(),
            3 => $"{BounceStrength:0.00}",
            4 => $"{AO:0.00}",
            5 => $"{LightIntensity:0.00}",
            6 => $"{LightDensity:0.00}",
            7 => ResolutionIndex switch { 0 => "320x240", 1 => "480x360", _ => "640x480" },
            8 => Lights ? "ON" : "OFF",
            9 => FpsLimit == 0 ? "UNCAP" : $"{FpsLimit} FPS",
            10 => Vsync ? "ON" : "OFF",
            11 => Volumetrics ? "ON" : "OFF",
            12 => $"{FogDensity:0.000}",
            13 => FogSteps.ToString(),
            14 => ProbeQuality switch { 0 => "48", 1 => "96", 2 => "144", _ => "192" },
            15 => ProbeBounces.ToString(),
            16 => HybridDirectSamples.ToString(),
            _ => ""
        };
    }
    float Fraction(int row)
    {
        if(Page == 8) return row switch
        {
            1 => (FlickerSettings.MinorInterval-8f)/112f,
            2 => FlickerSettings.MinorChance,
            3 => (FlickerSettings.MinorDuration-0.5f)/2.5f,
            5 => (FlickerSettings.EventInterval-20f)/280f,
            6 => (FlickerSettings.EventDuration-1f)/11f,
            7 => (FlickerSettings.EventSpeedMin-2f)/18f,
            8 => (FlickerSettings.EventSpeedMax-2f)/23f,
            9 => (FlickerSettings.EventOffChance-0.05f)/0.85f,
            12 => FlickerSettings.ChaseFogMax/0.1f,
            13 => (FlickerSettings.FogRise-0.2f)/7.8f,
            14 => (FlickerSettings.FogFall-0.2f)/7.8f,
            16 => (FlickerSettings.ChaseRedChance-0.08f)/0.92f,
            17 => FlickerSettings.BlackoutChance,
            _ => 0f
        };
        if(Page == 7) return row switch
        {
            2 => (Exposure-0.25f)/2.75f,
            4 => (BrightenSpeed-0.1f)/7.9f,5 => (DarkenSpeed-0.1f)/7.9f,
            7 => BloomStrength/2f,8 => (BloomThreshold-0.25f)/3.75f,
            9 => (BloomRadius-0.5f)/3.5f,_ => 0f
        };
        if(Page == 6) return row switch
        { 2 => (PlayerRed-0.05f)/0.95f,3 => (PlayerGreen-0.05f)/0.95f,4 => (PlayerBlue-0.05f)/0.95f,_ => 0f };
        if(Page == 5) return row switch
        {
            1 => (MonsterSettings.RoamSpeed-0.3f)/3.7f,
            2 => (MonsterSettings.SearchSpeed-0.5f)/7.5f,
            3 => (MonsterSettings.ChaseSpeed-2f)/12f,
            4 => (MonsterSettings.ChaseTurn-20f)/220f,
            5 => (MonsterSettings.SearchTurn-20f)/220f,
            6 => (MonsterSettings.RoamTurn-20f)/220f,
            7 => (MonsterSettings.SightRange-5f)/75f,
            8 => (MonsterSettings.SightAngle-45f)/315f,
            9 => (MonsterSettings.SearchRadius-0.5f)/4.5f,
            10 => (MonsterSettings.RoamRadius-5f)/55f,
            _ => 0f
        };
        if(Page == 4) return row switch
        {
            0 => (PickupRange-2f)/6f, 1 => (HoldDistance-0.35f)/5.65f,
            2 => (SpringFrequency-2f)/16f, 3 => (SpringDamping-0.3f)/1.7f,
            4 => (PidSmoothing-4f)/26f,5 => SlideResistance/12f,_ => 0f
        };
        if (Page == 0) return row switch
        {
            2 => (PaletteSteps - 2) / 30f,
            3 => Saturation / 2f,
            4 => (Temperature + 100) / 200f,
            5 => Contrast - 0.5f,
            6 => RedMix - 0.5f,
            7 => GreenMix - 0.5f,
            8 => BlueMix - 0.5f,
            _ => 0f
        };
        if (Page == 2) return row switch
        {
            0 => (WalkSpeed-1f)/7f,
            1 => (SprintMultiplier-1f)/2f,
            2 => (CrouchMultiplier-0.2f)/0.8f,
            3 => (Acceleration-1f)/14f,
            4 => (Deceleration-1f)/19f,
            5 => (StanceSmooth-1f)/11f,
            6 => (SprintSmooth-1f)/11f,
            7 => (CrouchHeight-0.8f)/0.7f,
            8 => (StepLength-1f)/2f,
            _ => 0f
        };
        if (Page == 3) return row switch
        {
            0 => (MouseSensitivity-0.0004f)/0.0046f,
            1 => IdleShake/4f,
            2 => WalkShake/8f,
            3 => SprintShake/8f,
            4 => CrouchShake/4f,
            5 => HeadBob/2f,
            6 => SwayAmount/2f,
            7 => StrafeRoll/5f,
            8 => TurnRoll/3f,
            9 => MovePitch/4f,
            10 => (CameraSmooth-1f)/19f,
            _ => 0f
        };
        return row switch
        {
            3 => BounceStrength / 3f,
            4 => AO / 1.5f,
            5 => (LightIntensity - 0.1f) / 1.9f,
            6 => LightDensity,
            1 => Math.Max(0,Array.IndexOf(SampleChoices,Samples))/4f,
            2 => (Bounces-1)/2f,
            7 => ResolutionIndex/2f,
            9 => FpsLimit == 0 ? 1f : (FpsLimit-15)/235f,
            12 => FogDensity / 0.1f,
            13 => (FogSteps - 2) / 6f,
            14 => ProbeQuality/3f,
            15 => (ProbeBounces-1)/5f,
            16 => (HybridDirectSamples-1)/7f,
            _ => 0f
        };
    }

    static void PixelText(Graphics g, string text, Brush brush, int x, int y)
    {
        DrawPixelText(g,text,Brushes.Black,x+1,y+1);
        DrawPixelText(g,text,brush,x,y);
    }
    static void DrawPixelText(Graphics g, string text, Brush brush, int x, int y)
    {
        foreach(char c in text.ToUpperInvariant())
        {
            if(Glyphs.TryGetValue(c,out var rows))
                for(int yy=0;yy<7;yy++) for(int xx=0;xx<5;xx++)
                    if((rows[yy] & (1 << (4-xx))) != 0) g.FillRectangle(brush,x+xx,y+yy,1,1);
            x += 6;
        }
    }
    static void DrawFrameCorners(Graphics g,Brush brush,RectangleF bounds,int offset)
    {
        int left=(int)bounds.Left+offset,top=(int)bounds.Top+offset;
        int right=(int)bounds.Right+offset,bottom=(int)bounds.Bottom+offset;
        int corner=Math.Clamp(Math.Min(right-left,bottom-top)/4,2,4);
        g.FillRectangle(brush,left,top,corner,1);
        g.FillRectangle(brush,left,top,1,corner);
        g.FillRectangle(brush,right-corner,top,corner,1);
        g.FillRectangle(brush,right-1,top,1,corner);
        g.FillRectangle(brush,left,bottom-1,corner,1);
        g.FillRectangle(brush,left,bottom-corner,1,corner);
        g.FillRectangle(brush,right-corner,bottom-1,corner,1);
        g.FillRectangle(brush,right-1,bottom-corner,1,corner);
    }
    void DrawPonder(Graphics g,Brush white,Brush paleBlue,Brush green)
    {
        g.FillRectangle(new SolidBrush(Color.FromArgb(218,5,10,13)),18,24,284,194);
        using var border=new Pen(Color.FromArgb(120,84,224,160));
        g.DrawLine(border,new PointF(18,24),new PointF(302,24));
        g.DrawLine(border,new PointF(302,24),new PointF(302,218));
        g.DrawLine(border,new PointF(302,218),new PointF(18,218));
        g.DrawLine(border,new PointF(18,218),new PointF(18,24));
        for(int y=28;y<216;y+=4) g.FillRectangle(new SolidBrush(Color.FromArgb(18,130,220,180)),20,y,280,1);
        string[] titles=["PONDER: BUILD GRID","PONDER: WIRE POWER","PONDER: WELD SHAPES","PONDER: MATERIALS"];
        string[] details=["PLACE DEVICES ON THE 5X5X5 GRID","G CONNECTS OUTPUT TO INPUT","K JOINS ANY TWO GRABBABLES","SELECT CONCRETE WOOD METAL PLASTIC"];
        PixelText(g,titles[PonderStep],green,28,34);
        PixelText(g,details[PonderStep],paleBlue,28,47);
        PointF Project(int x,int y,int z) => new(160+(x-z)*12,144+(x+z)*6-y*12);
        using var gridPen=new Pen(Color.FromArgb(96,120,196,207));
        for(int layer=0;layer<5;layer++)
        for(int line=0;line<=5;line++)
        {
            g.DrawLine(gridPen,Project(line,layer,0),Project(line,layer,5));
            g.DrawLine(gridPen,Project(0,layer,line),Project(5,layer,line));
        }
        for(int x=0;x<=5;x+=5)
        for(int z=0;z<=5;z+=5) g.DrawLine(gridPen,Project(x,0,z),Project(x,4,z));
        void Block(int x,int y,int z,Color color,string label)
        {
            PointF at=Project(x,y,z); using var fill=new SolidBrush(color);
            g.FillRectangle(fill,(int)at.X-6,(int)at.Y-6,13,13);
            PixelText(g,label,white,(int)at.X-3,(int)at.Y-3);
        }
        if(PonderStep==0) { Block(2,0,2,Color.FromArgb(155,181,108,46),"C");Block(3,1,2,Color.FromArgb(155,62,117,144),"T"); }
        else if(PonderStep==1) { Block(1,0,2,Color.FromArgb(180,190,54,40),"B");Block(3,0,2,Color.FromArgb(180,239,201,70),"L"); using var wire=new Pen(Color.FromArgb(210,84,224,160));g.DrawLine(wire,Project(1,0,2),Project(3,0,2)); }
        else if(PonderStep==2) { Block(1,0,2,Color.FromArgb(165,122,72,34),"P");Block(3,1,2,Color.FromArgb(165,122,72,34),"P");using var weld=new Pen(Color.FromArgb(220,236,244,247));g.DrawLine(weld,Project(1,0,2),Project(3,1,2)); }
        else { Block(1,0,2,Color.FromArgb(180,105,105,105),"C");Block(2,0,2,Color.FromArgb(180,142,82,38),"W");Block(3,0,2,Color.FromArgb(180,145,170,181),"M");Block(4,0,2,Color.FromArgb(180,82,156,190),"P"); }
        PixelText(g,"< LEFT RIGHT >    ESC CLOSE",paleBlue,64,202);
        PixelText(g,$"{PonderStep+1}/4",white,266,34);
    }

    internal Bitmap Draw()
    {
        var image = new Bitmap(320, 240, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.None;
        g.Clear(Color.Transparent);
        using var white = new SolidBrush(Color.FromArgb(237, 246, 247));
        using var paleBlue = new SolidBrush(Color.FromArgb(170, 211, 231));
        using var green = new SolidBrush(Color.FromArgb(84, 224, 160));
        using var unfilled = new SolidBrush(Color.FromArgb(130, 160, 171));
        if(Open && PonderPage)
        {
            DrawPonder(g,white,paleBlue,green);
        }
        else if(Open && SandboxPage)
        {
            PixelText(g,"BEANROOMS MENU",paleBlue,12,4);
            PixelText(g,"X",white,310,4);
            for(int category=0;category<SandboxCategories.Length;category++)
                PixelText(g,SandboxCategories[category],category==SandboxCategory?green:white,
                    12+category*75,17);
            SandboxKind[] items=SandboxItems[SandboxCategory];
            for(int slot=0;slot<SandboxRows;slot++)
            {
                int row=SandboxScroll+slot;
                if(row>=items.Length) break;
                int y=SandboxTop+slot*SandboxPitch;
                bool focused=row==SandboxSelected;
                bool spawnable=SandboxEnabled || items[row]==SandboxKind.Cube;
                Brush ink=spawnable?focused?green:white:unfilled;
                PixelText(g,$"{row+1}.",ink,21,y+3);
                PixelText(g,SandboxName(items[row]),ink,44,y+3);
                string value=spawnable?focused?"SPAWN <":"SPAWN":"LOCKED";
                PixelText(g,value,ink,316-value.Length*6,y+3);
            }
            if(SandboxScroll>0) PixelText(g,"^",paleBlue,8,SandboxTop-1);
            if(SandboxScroll<SandboxMaxScroll) PixelText(g,"v",paleBlue,8,SandboxTop+SandboxRows*SandboxPitch-3);
            PixelText(g,"CUBE COLOR",white,21,151);
            PixelText(g,"< "+CubeColors[CubeColorIndex].Name+" >",green,215,151);
            PixelText(g,"MATERIAL",white,21,171);
            PixelText(g,"< "+CubeMaterial.ToString().ToUpperInvariant()+" >",green,215,171);
            PixelText(g,"CLEAR CUBES",white,21,189);
            PixelText(g,"CLEAR",white,274,189);
            string detail=SandboxDetail(items[SandboxSelected]);
            PixelText(g,detail.Length>22?detail[..22]:detail,paleBlue,13,215);
            PixelText(g,NetworkConnected?(SandboxHost?"HOST CONTROLS":"GUEST CONTROLS"):"LOCAL SANDBOX",paleBlue,251,215);
            PixelText(g,"B CLOSE  G WIRE  K WELD  E USE",paleBlue,13,231);
        }
        else if(Open && TitleScreen)
        {
            PixelText(g,TitlePage==0?"BEANROOMS MENU":"BEANROOMS ONLINE",paleBlue,12,4);
            if(TitlePage==0)
            {
                PixelText(g,"SELECT MODE",white,44,43);
                string[] options=["PLAY SOLO","JOIN ONLINE","SETTINGS","EXIT"];
                for(int row=0;row<options.Length;row++)
                {
                    string label=options[row];
                    Brush ink=row==TitleSelected?green:white;
                    PixelText(g,$"{row+1}.",ink,21,72+row*24);
                    PixelText(g,label,ink,44,72+row*24);
                }
                PixelText(g,"UP DOWN SELECT  ENTER CONFIRM",paleBlue,13,225);
            }
            else
            {
                PixelText(g,"1.",TitleSelected==0?green:white,21,70);
                PixelText(g,"PHOTON ROOM",TitleSelected==0?green:white,44,70);
                string masked=JoinKey.Length==0?"ENTER KEY":new string('*',Math.Min(JoinKey.Length,20));
                PixelText(g,masked+(EditingJoinKey?"-":""),TitleSelected==0?green:white,44,83);
                PixelText(g,"2.",TitleSelected==1?green:white,21,125);
                PixelText(g,"CONNECT",TitleSelected==1?green:white,44,125);
                PixelText(g,"3.",TitleSelected==2?green:white,21,160);
                PixelText(g,"BACK",TitleSelected==2?green:white,44,160);
                PixelText(g,"STATUS "+(NetworkConnected?"ONLINE":NetworkStatus),paleBlue,13,225);
            }
        }
        else if (Open)
        {
            PixelText(g,"BEANROOMS MENU",paleBlue,12,4);
            PixelText(g,"RESET",white,270,4);
            PixelText(g,"X",white,310,4);
            for(int tab=0;tab<Tabs.Length;tab++)
                PixelText(g,Tabs[tab].Name,Tabs[tab].Page==2&&!MovementControlsWritable?unfilled:
                    Page==Tabs[tab].Page?green:white,
                    12+(tab%4)*75,17+(tab/4)*12);
            for (int row=0; row<Labels[Page].Length; row++)
            {
                int y = 54 + row * RowPitch;
                int textOffset = Page is 1 or 8 ? 1 : 3;
                bool focused=row==Selected;
                bool locked=Page==5&&!MonsterControlsWritable ||
                    Page==8&&!FlickerControlsWritable ||
                    Page==2&&!MovementControlsWritable ||
                    Page==9&&row==1&&!NetworkConnected&&!NetworkRemote ||
                    Page==9&&row==7&&NetworkConnected&&!SandboxSupported;
                Brush ink=locked?unfilled:focused?green:white;
                PixelText(g,$"{row+1}.",ink,21,y+textOffset);
                PixelText(g,Labels[Page][row],ink,44,y+textOffset);
                if (IsSlider(row))
                {
                    int filled=(int)MathF.Round(Math.Clamp(Fraction(row),0f,1f)*20f);
                    for(int tick=0;tick<20;tick++)
                        g.FillRectangle(tick<filled ? ink : unfilled,155+tick*5,y+textOffset+3,4,4);
                }
                string value=Value(row);
                if(IsChoice(row)) value="< "+value+" >";
                PixelText(g,value,ink,316-value.Length*6,y+textOffset);
            }
            PixelText(g,$"SEED {DebugSeed}",paleBlue,110,225);
            PixelText(g,$"FPS {Math.Clamp(FpsValue,0,999):000}",white,13,225);
            if((Page==5&&!MonsterControlsWritable)||(Page==8&&!FlickerControlsWritable))
                PixelText(g,"HOST ONLY",paleBlue,251,225);
            if(Page==2&&!MovementControlsWritable)
                PixelText(g,"SOLO ONLY",paleBlue,251,225);
        }
        else
        {
            if(WireTool)
                PixelText(g,"WIRE TOOL: CLICK SOURCE, THEN TARGET",green,13,225);
            else if(WeldTool)
                PixelText(g,"WELD TOOL: CLICK TWO OBJECTS",green,13,225);
            else if(!string.IsNullOrEmpty(ToolStatus))
                PixelText(g,ToolStatus,green,13,225);
            foreach(var tag in PlayerTags)
            {
                using var brush=new SolidBrush(tag.Ink);
                PixelText(g,tag.Label,brush,(int)tag.At.X-tag.Label.Length*3,(int)tag.At.Y);
            }
            if(ShowObjectFrames) foreach(var frame in ObjectFrames)
            {
                DrawFrameCorners(g,Brushes.Black,frame,1);
                DrawFrameCorners(g,white,frame,0);
            }
            foreach(var wire in SandboxWires)
            {
                using var pen=new Pen(wire.Ink);
                g.DrawLine(pen,wire.A,wire.B);
            }
            if(ShowDebugOverlay)
            {
                for(int line=0;line<DebugLines.Length;line++)
                    PixelText(g,DebugLines[line],line==0?green:line==1?paleBlue:white,13,10+line*12);
                PixelText(g,MonsterTargetText,paleBlue,190,10);
            }
            if(ShowDebugOverlay && MonsterDebug)
            {
                foreach(var segment in MonsterSegments)
                {
                    using var pen=new Pen(segment.Ink);
                    g.DrawLine(pen,segment.A,segment.B);
                }
                foreach(var point in MonsterPoints)
                {
                    using var brush=new SolidBrush(point.Ink);
                    g.FillRectangle(brush,point.At.X-2,point.At.Y-2,5,5);
                    PixelText(g,point.Label,brush,(int)point.At.X+4,(int)point.At.Y-3);
                }
            }
        }
        Dirty = false;
        return image;
    }

}
