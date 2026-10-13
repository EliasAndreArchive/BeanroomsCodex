namespace Beanrooms;

// The game uses one transport contract for solo play and Photon rooms.
internal interface INetClient : IDisposable
{
    bool Connected { get; }
    string Status { get; }
    int Id { get; }
    int ProtocolVersion { get; }
    void Start();
    void Service(float dt);
    void Send(NetMessage message);
    NetMessage? ConsumeWorld();
    NetMessage? ConsumeRespawn();
    NetMessage? ConsumeFlicker();
    NetMessage? ConsumePickupResult();
    CubeImpactEvent[] ConsumeCubeImpacts();
    (PlayerState[] Players,CubeState[] Cubes,MonsterState? Monster,
        MonsterTuning? MonsterTuning,int HostId) Snapshot();
}

internal sealed class SoloNetClient : INetClient
{
    public bool Connected => false;
    public string Status => "OFFLINE";
    public int Id => 0;
    public int ProtocolVersion => NetProtocol.Version;
    public void Start() { }
    public void Service(float dt) { }
    public void Send(NetMessage message) { }
    public NetMessage? ConsumeWorld() => null;
    public NetMessage? ConsumeRespawn() => null;
    public NetMessage? ConsumeFlicker() => null;
    public NetMessage? ConsumePickupResult() => null;
    public CubeImpactEvent[] ConsumeCubeImpacts() => [];
    public (PlayerState[] Players,CubeState[] Cubes,MonsterState? Monster,
        MonsterTuning? MonsterTuning,int HostId) Snapshot()
        => ([],[],null,null,0);
    public void Dispose() { }
}
