using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    internal enum MenuView { Pause, Categories, Settings, Help, Network }
    internal MenuView View = MenuView.Pause;
    MenuView settingsReturn=MenuView.Categories;
    internal int CategorySelected;
    internal bool Open, TitleScreen, SettingsFromTitle, Dithering = true, PaletteReduction = true, PostEnabled = true, AutoExposure, Bloom = true, Lights = true, Vsync = true, Dirty = true, Volumetrics = true;
    internal string ObjectPrompt="";
    internal string CommandLine="", CommandFeedback="";
    internal bool ShowFps;
    internal int TitlePage;
    internal int RenderMode = 2, ProbeQuality = 2, ProbeBounces = 4, HybridDirectSamples = 4;
    internal string RenderModeName => RenderMode == 1 ? "Realtime" : "Path traced";
    internal int Samples = 32, Bounces = 2, ResolutionIndex, FpsLimit;
    internal int TonemapMode = 2;
    internal float Exposure = 2f;
    internal float BloomStrength = 0.1f, BloomThreshold = 0.85f, BloomRadius = 1.9f;
    internal float BounceStrength = 1.75f, AO = 0.9f, LightIntensity = 1f, LightDensity = 0.65f, FogDensity = 0.008f;
    internal int FogSteps = 8;
    internal float MouseSensitivity = 0.00155f, IdleShake = 3f, WalkShake = 5f;
    internal float SprintShake = 7f, CrouchShake = 2f, HeadBob = 1f;
    internal float SwayAmount = 1.2f, StrafeRoll = 5f, TurnRoll = 1.5f, MovePitch = 4f, SwaySmooth = 11f, CameraSmooth = 11f;
    internal int Page, Selected;
    internal int FpsValue;
    internal string[] DebugLines = [];
    internal bool MonsterDebug;
    internal bool ShowDebugOverlay;
    internal bool ShowObjectFrames = true;
    internal bool ShowPlayerTags = true;
    internal bool MonsterControlsWritable = true;
    internal readonly MonsterTuning MonsterSettings = new();
    internal string PlayerName = "BEAN";
    internal float PlayerRed = 0.78f, PlayerGreen = 0.76f, PlayerBlue = 0.70f;
    internal int PlayerColorIndex = -1;
    internal bool NetworkConnected;
    internal string JoinKey = "", NetworkStatus = "OFFLINE";
    internal int NetworkId, NetworkHost, NetworkPlayers;
    internal string NetworkRoomCode="";
    internal Func<string,bool>? ChatAction;
    internal (PointF A,PointF B,Color Ink)[] MonsterSegments = [];
    internal (PointF At,string Label,Color Ink)[] MonsterPoints = [];
    internal (PointF At,string Label,Color Ink)[] PlayerTags = [];
    internal RectangleF[] ObjectFrames = [];
    internal string MonsterTargetText = "TARGET NONE";
    internal int BodyCount;
    internal Action? ConnectAction;
    internal Action<int>? TitleAction;
    internal Action<int>? SessionAction;
    internal Action? InteractionSound, HoverSound, InputSound, OutputSound;
    internal Action? CancelJoinAction;
    internal Func<CubeSpawnSpec,string>? SpawnAction;
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
    static readonly int[] SampleChoices = { 16, 32, 64, 128, 256 };
    bool developerCategories;
    static readonly (string Name,int Page)[] PlayerTabs =
    {
        ("DISPLAY",11), ("IMAGE",7), ("HUD",12), ("PROFILE",6)
    };
    static readonly (string Name,int Page)[] DeveloperTabs =
    {
        ("RENDER TUNING",1), ("MONSTER",5), ("DIAGNOSTICS",10)
    };
    (string Name,int Page)[] Tabs => developerCategories ? DeveloperTabs : PlayerTabs;
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
        ['[']=[14,8,8,8,8,8,14], [']']=[14,2,2,2,2,2,14],
        ['.']=[0,0,0,0,0,12,12], ['-']=[0,0,0,31,0,0,0],
        [',']=[0,0,0,0,0,4,8],
        ['!']=[4,4,4,4,4,0,4], ['?']=[14,17,1,2,4,0,4],
        ['\'']=[4,4,8,0,0,0,0], ['(']=[2,4,8,8,8,4,2], [')']=[8,4,2,2,2,4,8],
        ['+']=[0,4,4,31,4,4,0], [':']=[0,4,4,0,4,4,0],
        ['_']=[0,0,0,0,0,0,31], ['=']=[0,0,31,0,31,0,0]
    };
    static readonly string[][] Labels =
    {
        Array.Empty<string>(),
        new[] { "RENDER MODE", "PATH SAMPLES", "PATH BOUNCES", "BOUNCE LIGHT", "AO STRENGTH", "LIGHT INTENSITY", "LIGHT DENSITY", "RESOLUTION", "FIXTURES", "FPS LIMIT", "VSYNC", "VOLUMETRICS", "FOG DENSITY", "FOG STEPS", "PROBE GRID", "PROBE BOUNCES", "DIRECT SAMPLES" },
        Array.Empty<string>(),
        new[] { "MOUSE SENS", "IDLE SHAKE", "WALK SHAKE", "SPRINT SHAKE", "CROUCH SHAKE", "HEAD BOB", "SWAY AMOUNT", "STRAFE ROLL", "TURN ROLL", "MOVE PITCH", "LOOK SMOOTH" },
        Array.Empty<string>(),
        new[] { "MONSTER", "ROAM SPEED", "SEARCH SPEED", "CHASE SPEED", "CHASE TURN", "SEARCH TURN", "ROAM TURN", "SIGHT RANGE", "SIGHT ANGLE", "SEARCH RADIUS", "ROAM RADIUS" },
        new[] { "PLAYER NAME", "BEAN COLOR" },
        new[] { "POST FX", "EXPOSURE", "AUTO EXPOSURE", "BLOOM", "BLOOM STRENGTH", "BLOOM THRESHOLD", "BLOOM RADIUS", "DITHER", "POSTERIZATION" },
        Array.Empty<string>(),
        new[] { "RESUME", "PROFILE", "SETTINGS", "DEVELOPER", "HELP", "LEAVE SESSION", "QUIT GAME" },
        new[] { "FRAME RATE", "OBJECT OUTLINES", "PLAYER NAMES", "SYSTEM INFO", "MONSTER VECTORS" },
        new[] { "LIGHTING", "RESOLUTION", "FPS LIMIT", "VSYNC" },
        new[] { "FPS COUNTER", "OBJECT BRACKETS", "PLAYER NAMES" }
    };


}
