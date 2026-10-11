using System.Numerics;

namespace Beanrooms;

// Photon Realtime relays room events. The elected room master runs this same
// authoritative simulation formerly hosted by the standalone TCP server.
internal sealed class PhotonAuthority : IDisposable
{
    const float Step=1f/120f;
    readonly Dictionary<int,PhotonPeer> peers=[];
    readonly Queue<(int Id,NetMessage Message)> incoming=[];
    readonly List<(int Target,NetMessage Message)> outgoing=[];
    readonly PhysicsBodies physics=new();
    readonly SandboxWorld sandbox=new();
    readonly MonsterTuning monsterSettings=new();
    readonly FlickerTuning flickerSettings=new();
    List<System.Drawing.RectangleF> walls=[];
    MonsterBrain? monster;
    bool sandboxEnabled,blackout,blackoutAfterEvent;
    int seed=82317;
    float accumulator,snapshotTimer,flickerTimer,lastFlicker=-99f,deviceSnapshotAge;
    int lastSandboxSignature=int.MinValue;
    ChickenState chicken=new();
    float chickenClock, chickenNext=Random.Shared.Next(120,301);
    int chickenTargetId;

    internal PhotonAuthority(IEnumerable<int> playerIds)
    {
        ResetWorld();
        foreach(int id in playerIds.Order()) AddPlayer(id);
    }

    void ResetWorld()
    {
        physics.Clear(); sandbox.Clear();
        lastSandboxSignature=int.MinValue;deviceSnapshotAge=1f;
        walls=WorldLayout.Generate(seed).Walls;
        foreach(var plank in WorldLayout.CornerPlanks(seed,walls))
            physics.SpawnCornerPlank(plank.Position,plank.Yaw,walls,new Vector3(96f,1.68f,96f));
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
            X=peer.X,Y=peer.Y,Z=peer.Z,Blackout=blackout,BlackoutAfter=blackoutAfterEvent,
            FlickerAge=MathF.Max(-1f,flickerSettings.EventDuration-(flickerTimer-lastFlicker)) });
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
        UpdateChicken(dt);
        accumulator=Math.Min(accumulator+Math.Clamp(dt,0,.05f),.1f);
        while(accumulator>=Step)
        {
            sandbox.Update(Step,d=>physics.ApplyThruster(d.Position,d.Yaw,Step,d.Id),
                d=>physics.ApplyPiston(d.Position,d.Rotation,Step,d.Id),d=>physics.DevicePressed(d),
                d=>peers.Values.Any(p=>Vector3.DistanceSquared(d.Position,new Vector3(p.X,p.Y,p.Z))<3f*3f),
                d=>physics.ApplyMechanism(d,Step));
            foreach(PhotonPeer peer in peers.Values)
                if(physics.HeldBy(peer.Id)) { physics.SlideResistance=peer.SlideResistance; break; }
            if(physics.Bodies.Count>0)
                physics.Update(Step,new Vector3(96f,1.68f,96f),Vector3.UnitZ,Quaternion.Identity,walls,new Vector3(96f,1.68f,96f),
                    id=>peers.TryGetValue(id,out PhotonPeer? peer)
                        ? new GrabInput(new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),
                            CameraOrientation.FromAngles(peer.Yaw,peer.Pitch),peer.HoldDistance,
                            peer.SpringFrequency,peer.SpringDamping,peer.PidSmoothing,peer.GrabYaw,peer.GrabPitch,peer.SnapHold,peer.SnapStep)
                        : null,
                    peers.Values.Select(p=>new Vector3(p.X,p.Y,p.Z)).ToArray());
            physics.SyncSandboxDevices(sandbox.Devices);
            monster?.Update(Step,peers.Values.Select(p=>new PlayerState { Id=p.Id,X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw }).ToArray(),physics.Bodies);
            accumulator-=Step;
        }
        if(flickerSettings.RandomEvents && flickerTimer>=lastFlicker+flickerSettings.EventInterval)
            BroadcastFlicker(true);
        snapshotTimer+=dt;
        if(snapshotTimer>=.05f) { snapshotTimer=0; BroadcastSnapshot(); }
    }

    void UpdateChicken(float dt)
    {
        if(peers.Count==0) return;
        if(!chicken.Active)
        {
            chickenClock+=dt;
            if(chickenClock>=chickenNext) SpawnChicken();
            return;
        }
        chicken.Progress=Math.Clamp(chicken.Progress+dt/chicken.Duration,0f,1f);
        float x=chicken.StartX+(chicken.EndX-chicken.StartX)*chicken.Progress;
        float z=chicken.StartZ+(chicken.EndZ-chicken.StartZ)*chicken.Progress;
        chicken.X=x; chicken.Z=z;
        if(chicken.Progress>=1f)
        {
            chicken.Active=false;
            chickenClock=0f;
            chickenNext=Random.Shared.Next(120,301);
        }
    }

    void SpawnChicken()
    {
        PhotonPeer viewer=(chickenTargetId!=0&&peers.TryGetValue(chickenTargetId,out PhotonPeer? selected))
            ? selected : peers.Values.OrderBy(p=>p.Id).First();
        Vector3 forward=Look(viewer.Yaw,0f);
        Vector3 right=new(-forward.Z,0f,forward.X);
        Vector3 center=new Vector3(viewer.X,0.58f,viewer.Z)+forward*6f;
        Vector3 start=center+right*10f, end=center-right*10f;
        start.X=Math.Clamp(start.X,5f,187f); start.Z=Math.Clamp(start.Z,5f,187f);
        end.X=Math.Clamp(end.X,5f,187f); end.Z=Math.Clamp(end.Z,5f,187f);
        chicken=new ChickenState { Active=true,Id=Random.Shared.Next(1, int.MaxValue),Y=.58f,
            StartX=start.X,StartZ=start.Z,EndX=end.X,EndZ=end.Z,Progress=0f,Duration=12f,
            X=start.X,Z=start.Z,Yaw=MathF.Atan2(end.X-start.X,end.Z-start.Z) };
        chickenClock=0f;
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
                    peer.SnapHold=message.SnapHold;peer.SnapStep=Math.Clamp(message.SnapStep,.05f,1f);
                    if(IsHost(peer.Id)&&message.MonsterTuning!=null) { message.MonsterTuning.Clamp();monsterSettings.CopyFrom(message.MonsterTuning); }
                    if(IsHost(peer.Id)&&message.FlickerTuning!=null) message.FlickerTuning.Clamp();
                    if(IsHost(peer.Id)&&message.FlickerTuning!=null) flickerSettings.CopyFrom(message.FlickerTuning);
                    break;
                case "pickup":
                    if(!physics.HeldBy(peer.Id))
                    { physics.PickupRange=peer.PickupRange; physics.HoldDistance=peer.HoldDistance;
                      physics.TryPickup(new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),CameraOrientation.FromAngles(peer.Yaw,peer.Pitch),walls,peer.Id); }
                    Send(peer.Id,new NetMessage { Type="pickup_result",Action=physics.HeldCubeId(peer.Id),GrabSequence=message.GrabSequence }); break;
                case "release": physics.Release(peer.Id); break;
                case "grab_scroll": physics.NudgeGrabDistance(peer.Id,Math.Clamp(message.Action,-1,1)*.2f); break;
                case "spawn":
                    if(message.Action==1&&!sandboxEnabled) break;
                    physics.Spawn(new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),message.Action==1,walls,new Vector3(peer.X,peer.Y,peer.Z),new Vector3(Math.Clamp(message.R,0,1),Math.Clamp(message.G,0,1),Math.Clamp(message.B,0,1)),
                        Enum.IsDefined(message.Material)?message.Material:PhysicsMaterial.Wood); break;
                case "sandbox_toggle": if(IsHost(peer.Id)) sandboxEnabled=!sandboxEnabled; break;
                case "chicken_target":
                    if(IsHost(peer.Id)) chickenTargetId=message.Action>0&&peers.ContainsKey(message.Action)?message.Action:0;
                    break;
                case "device_spawn":
                    if(sandboxEnabled&&Enum.IsDefined((SandboxKind)message.Action))
                    { SandboxDevice? d=sandbox.Spawn((SandboxKind)message.Action,new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),peer.Yaw,walls);
                      if(d!=null&&!physics.SpawnSandboxDevice(d,walls,new Vector3(peer.X,peer.Y,peer.Z))) sandbox.Remove(d.Id); } break;
                case "copy_cube":
                    if(sandboxEnabled) physics.Spawn(new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),message.Action==1,walls,new Vector3(peer.X,peer.Y,peer.Z),new Vector3(Math.Clamp(message.R,0,1),Math.Clamp(message.G,0,1),Math.Clamp(message.B,0,1)),
                        Enum.IsDefined(message.Material)?message.Material:PhysicsMaterial.Wood); break;
                case "copy_device":
                    if(sandboxEnabled&&Enum.IsDefined((SandboxKind)message.Action))
                    { SandboxDevice? d=sandbox.Spawn((SandboxKind)message.Action,new Vector3(peer.X,peer.Y,peer.Z),Look(peer.Yaw,peer.Pitch),peer.Yaw,walls);
                      if(d!=null&&!physics.SpawnSandboxDevice(d,walls,new Vector3(peer.X,peer.Y,peer.Z))) sandbox.Remove(d.Id); } break;
                case "device_interact":
                    if(sandboxEnabled&&sandbox.Devices.Any(d=>d.Id==message.Action&&
                        Vector3.DistanceSquared(d.Position,new Vector3(peer.X,peer.Y,peer.Z))<6f*6f))
                        sandbox.Interact(message.Action);
                    break;
                case "wire":
                    if(sandboxEnabled&&sandbox.Devices.Any(d=>d.Id==message.FromId&&
                        Vector3.DistanceSquared(d.Position,new Vector3(peer.X,peer.Y,peer.Z))<6f*6f))
                        sandbox.Connect(message.FromId,message.ToId);
                    break;
                case "weld":
                    if(physics.Bodies.Any(b=>b.Id==message.FromId&&
                        Vector3.DistanceSquared(b.Position,new Vector3(peer.X,peer.Y,peer.Z))<6f*6f))
                        physics.TryWeld(message.FromId,message.ToId);
                    break;
                case "clear": if(IsHost(peer.Id)) { physics.Clear();sandbox.Clear(); } break;
                case "regen": if(IsHost(peer.Id)) Regenerate(); break;
                case "flicker": if(IsHost(peer.Id)) BroadcastFlicker(); break;
            }
        }
    }

    void Regenerate()
    {
        seed=Random.Shared.Next(1,1_000_000_000); ResetWorld(); int slot=0;
        foreach(PhotonPeer peer in peers.Values.OrderBy(p=>p.Id))
        { Vector2 spawn=SpawnForSlot(slot++);peer.X=spawn.X;peer.Y=1.68f;peer.Z=spawn.Y;
          Send(peer.Id,new NetMessage { Type="world",Seed=seed,X=peer.X,Y=peer.Y,Z=peer.Z,RespawnId=peer.RespawnId }); }
    }

    void BroadcastFlicker(bool random=false)
    {
        if(flickerTimer-lastFlicker<.75f) return;
        bool restoring=blackout||blackoutAfterEvent; blackout=false;
        blackoutAfterEvent=!restoring&&random&&Random.Shared.NextSingle()<flickerSettings.BlackoutChance;
        lastFlicker=flickerTimer; Broadcast(new NetMessage { Type="flicker",BlackoutAfter=blackoutAfterEvent });
    }

    int SandboxSignature()
    {
        var hash=new HashCode();
        foreach(SandboxDevice d in sandbox.Devices)
        { hash.Add(d.Id);hash.Add(d.Kind);hash.Add((int)(d.X*100));hash.Add((int)(d.Y*100));hash.Add((int)(d.Z*100));
          hash.Add((int)(d.Qx*1000));hash.Add((int)(d.Qy*1000));hash.Add((int)(d.Qz*1000));hash.Add((int)(d.Qw*1000));
          hash.Add(d.State);hash.Add(d.BodyId); }
        foreach(SandboxWire wire in sandbox.Wires) { hash.Add(wire.From);hash.Add(wire.To); }
        return hash.ToHashCode();
    }

    void BroadcastSnapshot()
    {
        int signature=SandboxSignature();
        bool devicesChanged=signature!=lastSandboxSignature||deviceSnapshotAge>=1f;
        if(devicesChanged) { lastSandboxSignature=signature;deviceSnapshotAge=0f; } else deviceSnapshotAge+=.05f;
        PlayerState[] players=peers.Values.Select(p=>new PlayerState { Id=p.Id,X=p.X,Y=p.Y,Z=p.Z,Yaw=p.Yaw,MoveSpeed=p.MoveSpeed,SprintBlend=p.SprintBlend,CrouchBlend=p.CrouchBlend,Name=p.Name,R=p.R,G=p.G,B=p.B }).ToArray();
        CubeState[] cubes=physics.Bodies.Where(b=>b.SandboxDeviceId==0).Select(b=>new CubeState { Id=b.Id,HolderId=physics.HolderOf(b),X=b.Position.X,Y=b.Position.Y,Z=b.Position.Z,Vx=b.Velocity.X,Vy=b.Velocity.Y,Vz=b.Velocity.Z,Ax=b.AngularVelocity.X,Ay=b.AngularVelocity.Y,Az=b.AngularVelocity.Z,Qx=b.Rotation.X,Qy=b.Rotation.Y,Qz=b.Rotation.Z,Qw=b.Rotation.W,Grounded=b.Grounded,SlipSpeed=PhysicsBodies.ContactSlipSpeed(b),HalfSize=b.HalfSize,Emissive=b.Emissive,Plank=b.Plank,Material=b.Material,R=b.Color.X,G=b.Color.Y,B=b.Color.Z }).ToArray();
        Broadcast(new NetMessage { Type="snapshot",Seed=seed,Players=players,Cubes=cubes,HostId=players.Select(p=>p.Id).DefaultIfEmpty(0).Min(),SandboxEnabled=sandboxEnabled,Devices=devicesChanged?sandbox.Devices.ToArray():[],Wires=devicesChanged?sandbox.Wires.ToArray():[],DevicesChanged=devicesChanged,CubeImpacts=physics.DrainCubeImpacts(),MonsterTuning=monsterSettings.Clone(),FlickerTuning=flickerSettings.Clone(),Monster=monster?.State,Chicken=chicken.Clone() });
    }

    public void Dispose() => physics.Dispose();

    sealed class PhotonPeer(int id)
    {
        internal int Id=id,RespawnId=0; internal float X=96,Y=1.68f,Z=96,Yaw=.63f,Pitch,HoldDistance=2.25f,GrabYaw,GrabPitch;
        internal float MoveSpeed,SprintBlend,CrouchBlend,R=.78f,G=.76f,B=.70f;
        internal string Name=$"BEAN{id}"; internal float PickupRange=5,SpringFrequency=8.5f,SpringDamping=1.35f,PidSmoothing=18,SlideResistance=4.5f,SnapStep=.25f; internal bool SnapHold;
    }
}
