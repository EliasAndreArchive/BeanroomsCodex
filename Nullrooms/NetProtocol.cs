namespace Beanrooms;

internal enum PhysicsMaterial { Concrete, Wood, Metal, Plastic }

internal static class NetProtocol
{
    // Bump when a server changes a feature whose absence would otherwise look like
    // a valid, but ignored, client command.
    internal const int Version=8;
}

internal sealed class NetMessage
{
    public string Type { get; set; } = "";
    public int ProtocolVersion { get; set; }
    public int Id { get; set; }
    public int Seed { get; set; }
    public int Action { get; set; }
    public int GrabSequence { get; set; }
    public int SpawnAmount { get; set; }
    public float SpawnSize { get; set; }
    public bool Emissive { get; set; }
    public float MoveSpeed { get; set; }
    public float SprintBlend { get; set; }
    public float CrouchBlend { get; set; }
    public int RespawnId { get; set; }
    public float FlickerAge { get; set; } = -1f;
    public bool Blackout { get; set; }
    public bool BlackoutAfter { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float HoldDistance { get; set; }
    public float GrabYaw { get; set; }
    public float GrabPitch { get; set; }
    public float PickupRange { get; set; }
    public float SpringFrequency { get; set; }
    public float SpringDamping { get; set; }
    public float PidSmoothing { get; set; }
    public float SlideResistance { get; set; }
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }
    public PhysicsMaterial Material { get; set; } = PhysicsMaterial.Wood;
    public string Name { get; set; } = "";
    public PlayerState[] Players { get; set; } = [];
    public CubeState[] Cubes { get; set; } = [];
    public CubeImpactEvent[] CubeImpacts { get; set; } = [];
    public MonsterState? Monster { get; set; }
    public MonsterTuning? MonsterTuning { get; set; }
    public int HostId { get; set; }
}

internal sealed class MonsterTuning
{
    public bool Enabled { get; set; }
    public float RoamSpeed { get; set; } = 1.65f;
    public float SearchSpeed { get; set; } = 3.2f;
    public float ChaseSpeed { get; set; } = 6.8f;
    public float ChaseTurn { get; set; } = 66f;
    public float SearchTurn { get; set; } = 86f;
    public float RoamTurn { get; set; } = 103f;
    public float SightRange { get; set; } = 35f;
    public float SightAngle { get; set; } = 200f;
    public float SearchRadius { get; set; } = 1.5f;
    public float RoamRadius { get; set; } = 30f;

    internal MonsterTuning Clone() => new()
    {
        Enabled=Enabled,RoamSpeed=RoamSpeed,SearchSpeed=SearchSpeed,ChaseSpeed=ChaseSpeed,
        ChaseTurn=ChaseTurn,SearchTurn=SearchTurn,RoamTurn=RoamTurn,
        SightRange=SightRange,SightAngle=SightAngle,SearchRadius=SearchRadius,RoamRadius=RoamRadius
    };
    internal bool CopyFrom(MonsterTuning source)
    {
        bool changed=Enabled!=source.Enabled||RoamSpeed!=source.RoamSpeed||
            SearchSpeed!=source.SearchSpeed||ChaseSpeed!=source.ChaseSpeed||
            ChaseTurn!=source.ChaseTurn||SearchTurn!=source.SearchTurn||RoamTurn!=source.RoamTurn||
            SightRange!=source.SightRange||SightAngle!=source.SightAngle||
            SearchRadius!=source.SearchRadius||RoamRadius!=source.RoamRadius;
        Enabled=source.Enabled;RoamSpeed=source.RoamSpeed;SearchSpeed=source.SearchSpeed;
        ChaseSpeed=source.ChaseSpeed;ChaseTurn=source.ChaseTurn;SearchTurn=source.SearchTurn;
        RoamTurn=source.RoamTurn;SightRange=source.SightRange;SightAngle=source.SightAngle;
        SearchRadius=source.SearchRadius;RoamRadius=source.RoamRadius;
        return changed;
    }
    internal void Clamp()
    {
        RoamSpeed=Limit(RoamSpeed,0.3f,4f,1.65f);
        SearchSpeed=Limit(SearchSpeed,0.5f,8f,3.2f);
        ChaseSpeed=Limit(ChaseSpeed,2f,14f,6.8f);
        ChaseTurn=Limit(ChaseTurn,20f,240f,66f);
        SearchTurn=Limit(SearchTurn,20f,240f,86f);
        RoamTurn=Limit(RoamTurn,20f,240f,103f);
        SightRange=Limit(SightRange,5f,80f,35f);
        SightAngle=Limit(SightAngle,45f,360f,200f);
        SearchRadius=Limit(SearchRadius,0.5f,5f,1.5f);
        RoamRadius=Limit(RoamRadius,5f,60f,30f);
    }
    static float Limit(float value,float minimum,float maximum,float fallback) =>
        float.IsFinite(value)?Math.Clamp(value,minimum,maximum):fallback;
}

internal sealed class MonsterState
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public int Mode { get; set; }
    public int TargetId { get; set; }
    public float GoalX { get; set; }
    public float GoalZ { get; set; }
    public bool HasLastSeen { get; set; }
    public float LastX { get; set; }
    public float LastZ { get; set; }
    public NavPoint[] Route { get; set; } = [];
}

internal sealed class NavPoint
{
    public float X { get; set; }
    public float Z { get; set; }
}

internal sealed class PlayerState
{
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float MoveSpeed { get; set; }
    public float SprintBlend { get; set; }
    public float CrouchBlend { get; set; }
    public string Name { get; set; } = "BEAN";
    public float R { get; set; } = 0.78f;
    public float G { get; set; } = 0.76f;
    public float B { get; set; } = 0.70f;
}

internal sealed class CubeState
{
    public int Id { get; set; }
    public int HolderId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Vx { get; set; }
    public float Vy { get; set; }
    public float Vz { get; set; }
    public float Ax { get; set; }
    public float Ay { get; set; }
    public float Az { get; set; }
    public bool Grounded { get; set; }
    // Speed of the floor contact points relative to the floor, not the cube centre.
    // The server measures it so online clients can play scraping without local physics.
    public float SlipSpeed { get; set; }
    public float Qx { get; set; }
    public float Qy { get; set; }
    public float Qz { get; set; }
    public float Qw { get; set; }
    public float HalfSize { get; set; }
    public bool Emissive { get; set; }
    public PhysicsMaterial Material { get; set; } = PhysicsMaterial.Wood;
    public float R { get; set; }
    public float G { get; set; }
    public float B { get; set; }
}

internal sealed class CubeImpactEvent
{
    public int CubeId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Strength { get; set; }
    public PhysicsMaterial Material { get; set; } = PhysicsMaterial.Wood;
}
