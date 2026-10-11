using System.Text.Json;
using Photon.Client;
using Photon.Realtime;

namespace Beanrooms;

// Cloud transport for online sessions. All gameplay messages retain the existing
// NetMessage shape; Photon only replaces socket discovery and relay transport.
internal sealed class PhotonNetClient : INetClient, IConnectionCallbacks, IMatchmakingCallbacks,
    IInRoomCallbacks, IOnEventCallback
{
    const byte CommandEvent=41,ServerEvent=42;
    const string DefaultAppId="1d8bf519-89af-4f1b-bff2-f8af50c192e2";
    readonly RealtimeClient client=new();
    readonly string room;
    readonly object stateLock=new();
    NetMessage? pendingWorld,pendingRespawn,pendingPickupResult,pendingFlicker;
    readonly Queue<CubeImpactEvent> pendingCubeImpacts=[];
    PlayerState[] players=[]; CubeState[] cubes=[]; MonsterState? monster;
    MonsterTuning? monsterTuning; FlickerTuning? flickerTuning;
    SandboxDevice[] devices=[]; SandboxWire[] wires=[]; bool sandboxEnabled;
    PhotonAuthority? authority;
    int id,hostId,protocolVersion;
    bool started,disposed;
    string status="OFFLINE";

    internal PhotonNetClient(string room)
    {
        this.room=string.IsNullOrWhiteSpace(room)?"BEANROOMS":room.Trim().ToUpperInvariant();
        client.AddCallbackTarget(this);
    }

    public bool Connected => client.InRoom;
    public string Status => status;
    public int Id => id;
    public int ProtocolVersion => protocolVersion;
    public bool SupportsSandbox => protocolVersion>=NetProtocol.Version;

    public void Start()
    {
        if(started||disposed) return;
        started=true; status="CONNECTING";
        string appId=Environment.GetEnvironmentVariable("BEANROOMS_PHOTON_APP_ID")??DefaultAppId;
        var settings=new AppSettings { AppIdRealtime=appId,AppVersion="beanrooms-1",FixedRegion="eu",
            Protocol=ConnectionProtocol.Udp,EnableProtocolFallback=true,NetworkLogging=LogLevel.Error,ClientLogging=LogLevel.Warning };
        if(!client.ConnectUsingSettings(settings)) status="PHOTON UNAVAILABLE";
    }

    public void Service(float dt)
    {
        if(disposed||!started) return;
        client.Service();
        if(authority==null||!client.InRoom||client.CurrentRoom?.MasterClientId!=id) return;
        authority.Update(dt);
        foreach(var output in authority.DrainOutgoing()) DeliverAuthorityMessage(output.Target,output.Message);
    }

    void DeliverAuthorityMessage(int target,NetMessage message)
    {
        if(target==0 || target==id) ReceiveMessage(message);
        if(target==id) return;
        var args=target==0 ? new RaiseEventArgs { Receivers=ReceiverGroup.Others } : new RaiseEventArgs { TargetActors=[target] };
        bool reliable=message.Type!="snapshot";
        client.OpRaiseEvent(ServerEvent,JsonSerializer.Serialize(message),args,new SendOptions { Reliability=reliable });
    }

    public void Send(NetMessage message)
    {
        if(!Connected) return;
        if(authority!=null&&client.CurrentRoom?.MasterClientId==id) { authority.Receive(id,message); return; }
        client.OpRaiseEvent(CommandEvent,JsonSerializer.Serialize(message),new RaiseEventArgs { Receivers=ReceiverGroup.MasterClient },
            new SendOptions { Reliability=message.Type!="state" });
    }

    public void OnConnected() { }
    public void OnConnectedToMaster()
    {
        var roomOptions=new RoomOptions { MaxPlayers=4,EmptyRoomTtl=0,PlayerTtl=0 };
        if(!client.OpJoinOrCreateRoom(new EnterRoomArgs { RoomName=room,RoomOptions=roomOptions })) status="ROOM JOIN FAILED";
    }
    public void OnDisconnected(DisconnectCause cause)
    {
        if(!disposed) status=cause==DisconnectCause.InvalidAuthentication?"PHOTON APP ID REJECTED":"PHOTON DISCONNECTED";
        authority?.Dispose(); authority=null; id=hostId=protocolVersion=0;
    }
    public void OnRegionListReceived(RegionHandler regionHandler) { }
    public void OnCustomAuthenticationResponse(Dictionary<string,object> data) { }
    public void OnCustomAuthenticationFailed(string debugMessage) { status="PHOTON AUTH FAILED"; }

    public void OnJoinedRoom()
    {
        id=client.LocalPlayer?.ActorNumber??0; protocolVersion=NetProtocol.Version; status="ONLINE";
        EnsureAuthority();
    }
    public void OnCreatedRoom() { }
    public void OnCreateRoomFailed(short returnCode,string message) { status="ROOM CREATE FAILED"; }
    public void OnJoinRoomFailed(short returnCode,string message) { status="ROOM JOIN FAILED"; }
    public void OnJoinRandomFailed(short returnCode,string message) { status="ROOM JOIN FAILED"; }
    public void OnLeftRoom() { authority?.Dispose();authority=null; }
    public void OnPlayerEnteredRoom(Player newPlayer)
    {
        EnsureAuthority(); authority?.AddPlayer(newPlayer.ActorNumber);
    }
    public void OnPlayerLeftRoom(Player otherPlayer) => authority?.RemovePlayer(otherPlayer.ActorNumber);
    public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged) { }
    public void OnPlayerPropertiesUpdate(Player targetPlayer,PhotonHashtable changedProps) { }
    public void OnMasterClientSwitched(Player newMasterClient)
    {
        EnsureAuthority();
    }
    public void OnFriendListUpdate(List<FriendInfo> friendList) { }

    void EnsureAuthority()
    {
        if(!client.InRoom||client.CurrentRoom?.MasterClientId!=id||authority!=null) return;
        authority=new PhotonAuthority(client.CurrentRoom.Players.Keys);
    }

    public void OnEvent(EventData photonEvent)
    {
        if(photonEvent.CustomData is not string encoded) return;
        NetMessage? message;
        try { message=JsonSerializer.Deserialize<NetMessage>(encoded); }
        catch(JsonException) { return; }
        if(message==null) return;
        if(photonEvent.Code==CommandEvent)
        {
            if(authority!=null&&photonEvent.Sender>0) authority.Receive(photonEvent.Sender,message);
            return;
        }
        if(photonEvent.Code==ServerEvent && photonEvent.Sender==client.CurrentRoom?.MasterClientId) ReceiveMessage(message);
    }

    void ReceiveMessage(NetMessage message)
    {
        lock(stateLock)
        {
            switch(message.Type)
            {
                case "welcome": id=message.Id;protocolVersion=message.ProtocolVersion;pendingWorld=message;break;
                case "world": pendingWorld=message;break;
                case "respawn": pendingRespawn=message;break;
                case "pickup_result": pendingPickupResult=message;break;
                case "flicker": pendingFlicker=message;break;
                case "snapshot":
                    players=message.Players;cubes=message.Cubes;monster=message.Monster;monsterTuning=message.MonsterTuning;flickerTuning=message.FlickerTuning;
                    hostId=message.HostId;devices=message.Devices;wires=message.Wires;sandboxEnabled=message.SandboxEnabled;
                    foreach(CubeImpactEvent impact in message.CubeImpacts) pendingCubeImpacts.Enqueue(impact);
                    while(pendingCubeImpacts.Count>128) pendingCubeImpacts.Dequeue();
                    break;
            }
        }
    }

    public NetMessage? ConsumeWorld() { lock(stateLock) { var result=pendingWorld;pendingWorld=null;return result; } }
    public NetMessage? ConsumeRespawn() { lock(stateLock) { var result=pendingRespawn;pendingRespawn=null;return result; } }
    public NetMessage? ConsumeFlicker() { lock(stateLock) { var result=pendingFlicker;pendingFlicker=null;return result; } }
    public NetMessage? ConsumePickupResult() { lock(stateLock) { var result=pendingPickupResult;pendingPickupResult=null;return result; } }
    public CubeImpactEvent[] ConsumeCubeImpacts() { lock(stateLock) { var result=pendingCubeImpacts.ToArray();pendingCubeImpacts.Clear();return result; } }
    public (PlayerState[] Players,CubeState[] Cubes,MonsterState? Monster,MonsterTuning? MonsterTuning,FlickerTuning? FlickerTuning,int HostId,SandboxDevice[] Devices,SandboxWire[] Wires,bool SandboxEnabled) Snapshot()
    { lock(stateLock) return (players,cubes,monster,monsterTuning,flickerTuning,hostId,devices,wires,sandboxEnabled); }

    public void Dispose()
    {
        if(disposed) return; disposed=true; status="OFFLINE";
        if(client.IsConnected) client.Disconnect();
        authority?.Dispose();authority=null;client.RemoveCallbackTarget(this);
    }
}
