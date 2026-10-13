using System.Numerics;

namespace Beanrooms;

// Photon Realtime relays room events. The elected room master simulates shared physics.
internal sealed class PhotonAuthority : IDisposable
{
    const float Step=1f/120f;
    readonly Dictionary<int,PhotonPeer> peers=[];
    readonly Queue<(int Id,NetMessage Message)> incoming=[];
    readonly List<(int Target,NetMessage Message)> outgoing=[];
    readonly PhysicsBodies physics=new();
    readonly MonsterTuning monsterSettings=new();
    List<System.Drawing.RectangleF> walls=[];
    MonsterBrain? monster;
    bool blackoutAfterEvent;
    int seed=82317;
    float accumulator,snapshotTimer,flickerTimer,lastFlicker=-99f;

    internal PhotonAuthority(IEnumerable<int> playerIds)
    {
        ResetWorld();
        foreach(int id in playerIds.Order()) AddPlayer(id);
    }

    void ResetWorld()
    {
        physics.Clear();
        walls=WorldLayout.Generate(seed).Walls;
        WorldProps.Place(physics,walls);
        monster=new MonsterBrain(seed,walls,monsterSettings);
    }

    Vector2 SpawnForSlot(int slot) => WorldLayout.FindClearSpawn(walls,new Vector2(96f+slot*1.5f,96f),.4f);

    internal void AddPlayer(int id)
    {
        if(peers.ContainsKey(id)) return;
        Vector2 spawn=SpawnForSlot(peers.Count);
        var peer=new PhotonPeer(id) { X=spawn.X,Z=spawn.Y };
        peers.Add(id,peer);
        Send(id,new NetMessage { Type="welcome",ProtocolVersion=NetProtocol.Version,Id=id,Seed=seed,
            X=peer.X,Y=peer.Y,Z=peer.Z,Blackout=blackoutAfterEvent,BlackoutAfter=blackoutAfterEvent,
            FlickerAge=MathF.Max(-1f,FlickerProfile.EventDuration-(flickerTimer-lastFlicker)) });
    }

    internal void RemovePlayer(int id)
    {
        if(peers.Remove(id)) physics.Release(id);
    }

    internal void Receive(int id,NetMessage message) => incoming.Enqueue((id,message));

    internal (int Target,NetMessage Message)[] DrainOutgoing()
    {
        var result=outgoing.ToArray(); outgoing.Clear(); return result;
    }

    void Send(int id,NetMessage message) => outgoing.Add((id,message));
    void Broadcast(NetMessage message) => outgoing.Add((0,message));
    bool IsHost(int id) => peers.Count>0 && id==peers.Keys.Min();
    static Vector3 Look(float yaw,float pitch) => CameraOrientation.Forward(yaw,pitch);

    internal void Update(float dt)
    {
        ProcessMessages();
        flickerTimer+=dt;
        accumulator=Math.Min(accumulator+Math.Clamp(dt,0,.05f),.1f);
        while(accumulator>=Step)
        {
            foreach(PhotonPeer peer in peers.Values)
                if(physics.HeldBy(peer.Id)) { physics.SlideResistance=peer.SlideResistance; break; }
            if(physics.Bodies.Count>0)
                physics.Update(Step,new Vector3(96f,1.68f,96f),Vector3.UnitZ,Quaternion.Identity,walls,new Vector3(96f,1.68f,96f),
                    id=>peers.TryGetValue(id,out PhotonPeer? peer)
                        ? new GrabInput(new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),
                            CameraOrientation.FromAngles(peer.Yaw,peer.Pitch),peer.HoldDistance,
                            peer.SpringFrequency,peer.SpringDamping,peer.PidSmoothing,peer.GrabYaw,peer.GrabPitch)
                        : null,
                    peers.Values.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToArray());
            monster?.Update(Step,peers.Values.Select(p=>new PlayerState { Id=p.Id,X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw }).ToArray(),physics.Bodies);
            accumulator-=Step;
        }
        if(flickerTimer>=lastFlicker+FlickerProfile.EventInterval)
            BroadcastFlicker();
        snapshotTimer+=dt;
        if(snapshotTimer>=.05f) { snapshotTimer=0; BroadcastSnapshot(); }
    }

    void ProcessMessages()
    {
        while(incoming.TryDequeue(out var entry))
        {
            if(!peers.TryGetValue(entry.Id,out PhotonPeer? peer)) continue;
            NetMessage message=entry.Message;
            switch(message.Type)
            {
                case "state":
                    if(float.IsFinite(message.X)&&float.IsFinite(message.Y)&&float.IsFinite(message.Z)&&message.RespawnId==peer.RespawnId)
                    { peer.X=Math.Clamp(message.X,4.3f,187.7f);peer.Y=Math.Clamp(message.Y,.8f,1.9f);peer.Z=Math.Clamp(message.Z,4.3f,187.7f); }
                    peer.Yaw=float.IsFinite(message.Yaw)?message.Yaw:peer.Yaw;
                    peer.Pitch=float.IsFinite(message.Pitch)?Math.Clamp(message.Pitch,-1.45f,1.45f):peer.Pitch;
                    peer.MoveSpeed=float.IsFinite(message.MoveSpeed)?Math.Clamp(message.MoveSpeed,0,10):0;
                    peer.SprintBlend=float.IsFinite(message.SprintBlend)?Math.Clamp(message.SprintBlend,0,1):0;
                    peer.CrouchBlend=float.IsFinite(message.CrouchBlend)?Math.Clamp(message.CrouchBlend,0,1):0;
                    string name=new string((message.Name??"").ToUpperInvariant().Where(c=>char.IsAsciiLetterOrDigit(c)||c is ' ' or '-').Take(12).ToArray()).Trim();
                    peer.Name=name.Length>0?name:$"BEAN{peer.Id}";
                    if(float.IsFinite(message.R)&&float.IsFinite(message.G)&&float.IsFinite(message.B))
                    { peer.R=Math.Clamp(message.R,.05f,1);peer.G=Math.Clamp(message.G,.05f,1);peer.B=Math.Clamp(message.B,.05f,1); }
                    peer.HoldDistance=Math.Clamp(message.HoldDistance,.35f,6); peer.GrabYaw=Math.Clamp(message.GrabYaw,-6.28f,6.28f); peer.GrabPitch=Math.Clamp(message.GrabPitch,-1.5f,1.5f);
                    if(message.PickupRange>0) peer.PickupRange=Math.Clamp(message.PickupRange,1,10);
                    if(message.SpringFrequency>0) peer.SpringFrequency=Math.Clamp(message.SpringFrequency,1,30);
                    if(message.SpringDamping>0) peer.SpringDamping=Math.Clamp(message.SpringDamping,.1f,5);
                    if(message.PidSmoothing>0) peer.PidSmoothing=Math.Clamp(message.PidSmoothing,1,50);
                    if(message.SlideResistance>0) peer.SlideResistance=Math.Clamp(message.SlideResistance,.1f,20);
                    if(IsHost(peer.Id)&&message.MonsterTuning!=null) { message.MonsterTuning.Clamp();monsterSettings.CopyFrom(message.MonsterTuning); }
                    break;
                case "pickup":
                    if(!physics.HeldBy(peer.Id))
                    { physics.PickupRange=peer.PickupRange; physics.HoldDistance=peer.HoldDistance;
                      physics.TryPickup(new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),CameraOrientation.FromAngles(peer.Yaw,peer.Pitch),walls,peer.Id); }
                    Send(peer.Id,new NetMessage { Type="pickup_result",Action=physics.HeldCubeId(peer.Id),GrabSequence=message.GrabSequence }); break;
                case "release": physics.Release(peer.Id); break;
                case "grab_scroll": physics.NudgeGrabDistance(peer.Id,Math.Clamp(message.Action,-1,1)*.2f); break;
            }
        }
    }

    void BroadcastFlicker()
    {
        if(flickerTimer-lastFlicker<.75f) return;
        bool restoring=blackoutAfterEvent;
        blackoutAfterEvent=!restoring&&Random.Shared.NextSingle()<FlickerProfile.BlackoutChance;
        lastFlicker=flickerTimer; Broadcast(new NetMessage { Type="flicker",BlackoutAfter=blackoutAfterEvent });
    }

    void BroadcastSnapshot()
    {
        PlayerState[] players=peers.Values.Select(p=>new PlayerState { Id=p.Id,X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw,MoveSpeed=p.MoveSpeed,SprintBlend=p.SprintBlend,CrouchBlend=p.CrouchBlend,Name=p.Name,R=p.R,G=p.G,B=p.B }).ToArray();
        CubeState[] cubes=physics.Bodies.Select(b=>new CubeState { Id=b.Id,HolderId=physics.HolderOf(b),X=b.Position.X,Y=b.Position.Y,Z=b.Position.Z,Vx=b.Velocity.X,Vy=b.Velocity.Y,Vz=b.Velocity.Z,Ax=b.AngularVelocity.X,Ay=b.AngularVelocity.Y,Az=b.AngularVelocity.Z,Qx=b.Rotation.X,Qy=b.Rotation.Y,Qz=b.Rotation.Z,Qw=b.Rotation.W,Grounded=b.Grounded,SlipSpeed=PhysicsBodies.ContactSlipSpeed(b),HalfSize=b.HalfSize,Emissive=b.Emissive,Material=b.Material,R=b.Color.X,G=b.Color.Y,B=b.Color.Z }).ToArray();
        Broadcast(new NetMessage { Type="snapshot",Seed=seed,Players=players,Cubes=cubes,HostId=players.Select(p=>p.Id).DefaultIfEmpty(0).Min(),CubeImpacts=physics.DrainCubeImpacts(),MonsterTuning=monsterSettings.Clone(),Monster=monster?.State });
    }

    public void Dispose() => physics.Dispose();

    sealed class PhotonPeer(int id)
    {
        internal int Id=id,RespawnId=0; internal float X=96,Y=1.68f,Z=96,Yaw=.63f,Pitch,
            HoldDistance=ObjectInteractionProfile.HoldDistance,GrabYaw,GrabPitch;
        internal float MoveSpeed,SprintBlend,CrouchBlend,R=.78f,G=.76f,B=.70f;
        internal string Name=$"BEAN{id}";
        internal float PickupRange=ObjectInteractionProfile.PickupRange,
            SpringFrequency=ObjectInteractionProfile.SpringFrequency,
            SpringDamping=ObjectInteractionProfile.SpringDamping,
            PidSmoothing=ObjectInteractionProfile.Smoothing,
            SlideResistance=ObjectInteractionProfile.SlideResistance;
    }
}
