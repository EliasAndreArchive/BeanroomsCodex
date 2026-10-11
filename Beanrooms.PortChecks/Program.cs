using Beanrooms;
using StbImageSharp;
using System.Drawing;
using System.Text.Json;

var menu=new DebugMenu();
var blackoutTuning=new FlickerTuning { MinorEnabled=false,EventDuration=2f };
var blackoutCheck=new LightFlicker(0,blackoutTuning);
blackoutCheck.Trigger(1,true);
if(blackoutCheck.IsOn(5,5,1,4)) throw new Exception("Fixture did not stay dark after blackout event");
blackoutCheck.Trigger(5);
if(!blackoutCheck.IsOn(5,5,1,8)) throw new Exception("Next flicker did not restore fixture");
menu.ShowTitle();
using var image=menu.Draw();
if(image.Width!=320||image.Height!=240 || !image.Pixels.Where((_,i)=>i%4==3).Any(alpha=>alpha!=0))
    throw new Exception("The menu did not render into its 320x240 overlay");
menu.LeaveTitle();menu.Open=true;menu.NetworkConnected=true;menu.SandboxHost=true;menu.SetPage(9);menu.Selected=7;
int sandboxToggles=0;menu.SandboxToggle=()=>sandboxToggles++;
menu.AdjustSelected(1);
if(sandboxToggles!=1) throw new Exception("Host session menu did not expose sandbox mode");
menu.SandboxHost=false;menu.AdjustSelected(1);
if(sandboxToggles!=2) throw new Exception("Guest session menu did not send a server-authorized sandbox request");
menu.SandboxSupported=false;menu.AdjustSelected(1);
if(sandboxToggles!=2) throw new Exception("Unsupported server still exposed sandbox mode");
menu.NetworkConnected=false;menu.AdjustSelected(1);
if(sandboxToggles!=3) throw new Exception("Solo session menu did not expose sandbox mode");
menu.SetPage(9);menu.ChickenControlVisible=false;menu.Selected=7;int chickenTargetChanges=0;
menu.ChickenTargetAction= _=>chickenTargetChanges++;
menu.AdjustSelected(1);
if(chickenTargetChanges!=0) throw new Exception("Guest-only session menu exposed chicken targeting");
menu.ChickenControlVisible=true;menu.Selected=8;menu.AdjustSelected(1);
if(chickenTargetChanges!=1) throw new Exception("Host session menu did not expose chicken targeting");
menu.SetPage(4);menu.Selected=6;menu.AdjustSelected(1);
if(!menu.SnapHold) throw new Exception("Physics menu did not toggle grid snap");
menu.Selected=7;float snapStep=menu.SnapStep;menu.AdjustSelected(1);
if(menu.SnapStep<=snapStep) throw new Exception("Physics menu did not adjust snap step");
using(var staleClient=new LocalNetClient())
{
    var receive=typeof(LocalNetClient).GetMethod("ReceiveMessage",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    // A pre-version server omits ProtocolVersion, which deserializes as zero.
    receive.Invoke(staleClient,["{\"Type\":\"welcome\",\"Id\":1}"]);
    if(staleClient.SupportsSandbox || staleClient.Status!="SERVER UPDATE REQUIRED")
        throw new Exception("A stale server did not report its required update");
}
using(var photonAuthority=new PhotonAuthority([4,9]))
{
    // The Photon room master consumes the same messages as the dedicated server.
    photonAuthority.Update(.06f);
    var welcome=photonAuthority.DrainOutgoing();
    if(!welcome.Any(entry=>entry.Target==4&&entry.Message.Type=="welcome") ||
       !welcome.Any(entry=>entry.Target==9&&entry.Message.Type=="welcome"))
        throw new Exception("Photon authority did not welcome every room player");
    photonAuthority.Receive(4,new NetMessage { Type="sandbox_toggle" });
    photonAuthority.Update(.06f);
    var enabled=photonAuthority.DrainOutgoing().Last(entry=>entry.Target==0&&entry.Message.Type=="snapshot").Message;
    if(!enabled.SandboxEnabled||enabled.HostId!=4)
        throw new Exception("Photon authority did not apply the room-master sandbox toggle");
    photonAuthority.Receive(9,new NetMessage { Type="sandbox_toggle" });
    photonAuthority.Update(.06f);
    var guestAttempt=photonAuthority.DrainOutgoing().Last(entry=>entry.Target==0&&entry.Message.Type=="snapshot").Message;
    if(!guestAttempt.SandboxEnabled)
        throw new Exception("Photon authority accepted a guest sandbox toggle");
}
{
    var source=new NetMessage { Type="snapshot",Chicken=new ChickenState
        { Active=true,Id=7,X=12.5f,Y=.58f,Z=19f,StartX=22f,StartZ=19f,EndX=2f,EndZ=19f,Progress=.4f,Duration=12f } };
    var roundTrip=JsonSerializer.Deserialize<NetMessage>(JsonSerializer.Serialize(source));
    if(roundTrip?.Chicken is not { Active:true } egg || egg.Id!=7 || MathF.Abs(egg.Progress-.4f)>.001f)
        throw new Exception("Chicken easter egg state did not survive protocol serialization");
}
{
    var sandbox=new SandboxWorld();
    var plate=new SandboxDevice { Id=1,Kind=SandboxKind.PressurePlate };
    var delay=new SandboxDevice { Id=2,Kind=SandboxKind.Delay };
    var lever=new SandboxDevice { Id=3,Kind=SandboxKind.Lever };
    var counter=new SandboxDevice { Id=4,Kind=SandboxKind.Counter };
    sandbox.Devices.AddRange([plate,delay,lever,counter]);
    sandbox.Connect(1,2);sandbox.Connect(3,4);
    sandbox.Update(.1f,platePressed:_=>true);
    if(!plate.State) throw new Exception("Pressure plate did not report a nearby cube");
    for(int i=0;i<7;i++) sandbox.Update(.1f,platePressed:_=>true);
    if(!delay.State) throw new Exception("Delay gate did not hold then pass its input");
    for(int pulse=0;pulse<3;pulse++)
    {
        lever.State=true;sandbox.Update(.02f);
        lever.State=false;sandbox.Update(.02f);
    }
    if(!counter.State) throw new Exception("Counter did not fire on its third pulse");
}
{
    var sandbox=new SandboxWorld();
    var powered=new HashSet<SandboxKind>();
    sandbox.Devices.AddRange([
        new SandboxDevice { Id=1,Kind=SandboxKind.Hinge,State=true },
        new SandboxDevice { Id=2,Kind=SandboxKind.Wheel,State=true },
        new SandboxDevice { Id=3,Kind=SandboxKind.Rope,State=true },
        new SandboxDevice { Id=4,Kind=SandboxKind.Conveyor },
        new SandboxDevice { Id=5,Kind=SandboxKind.SensorLight },
        new SandboxDevice { Id=6,Kind=SandboxKind.Lever,State=true }
    ]);
    sandbox.Connect(6,1);sandbox.Connect(6,2);sandbox.Connect(6,3);sandbox.Connect(6,4);
    sandbox.Update(.1f,playerNearby:_=>true,poweredMechanism:device=>powered.Add(device.Kind));
    if(!sandbox.Devices.Single(device=>device.Id==5).State ||
       !powered.SetEquals([SandboxKind.Hinge,SandboxKind.Wheel,SandboxKind.Rope,SandboxKind.Conveyor]))
        throw new Exception("Sandbox mechanisms did not receive their powered update");
}
{
    var layout=WorldLayout.Generate(82317);
    using var physics=new PhysicsBodies();
    foreach(var plank in WorldLayout.CornerPlanks(82317,layout.Walls))
        physics.SpawnCornerPlank(plank.Position,plank.Yaw,layout.Walls,new System.Numerics.Vector3(96f,1.68f,96f));
    RigidCube? pallet=physics.Bodies.FirstOrDefault(body=>body.Plank);
    if(pallet==null || pallet.HalfSize<.89f) throw new Exception("Seeded world did not place a full-size corner pallet");
    pallet.Rotation=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX,MathF.PI*.5f);
    System.Numerics.Vector3 tippedExtents=PhysicsBodies.Extents(pallet);
    if(tippedExtents.Y<.89f || tippedExtents.Z>.12f) throw new Exception("Pallet collider did not rotate with its low side");
}
foreach(string asset in new[]{"wall.png","floor.png","ceiling.png","beanface.png","eye.png"})
{
    string path=Path.Combine(AppContext.BaseDirectory,"assets",asset);
    var decoded=ImageResult.FromMemory(File.ReadAllBytes(path),ColorComponents.RedGreenBlueAlpha);
    if(decoded.Width==0 || decoded.Height==0) throw new Exception("Failed to decode "+asset);
}
using(var audio=new AudioSystem())
foreach(string asset in new[]{"spawn-boom.mp3","ui-click.wav","ui-hover.wav","lamp-buzz.mp3",
    "monster.wav","footstep_concrete_000.wav","footstep_concrete_001.wav",
    "footstep_concrete_002.wav","footstep_concrete_003.wav","footstep_concrete_004.wav",
    "cube_wood_impact_000.wav","cube_wood_impact_001.wav",
    "cube_wood_impact_002.wav","cube_wood_impact_003.wav","cube_wood_impact_004.wav",
    "cube_concrete_scrape.wav"})
{
    using var clip=audio.Load(asset);
    if(clip.Duration<=0) throw new Exception("Failed to decode "+asset);
}
using(var audio=new AudioSystem())
{
    if(audio.Reverb==null) throw new Exception("OpenAL Soft did not create the room reverb effect");
    if(audio.Reverb.GroupCapacity<1) throw new Exception("OpenAL Soft did not create a grouped EAX slot");
    using var clip=audio.Load("monster.wav");
    using var voice=new AudioVoice(clip);
    voice.SetHighFrequencyGain(0.5f);
    if(audio.Al.GetError()!=Silk.NET.OpenAL.AudioError.NoError)
        throw new Exception("OpenAL could not apply the acoustic low pass filter");
}
using(var audio=new AudioSystem())
using(var physical=new PhysicalSounds(audio))
{
    var listener=new System.Numerics.Vector3(10f,1.68f,10f);
    var walker=new FootstepSource(0,listener,4f,0f,0f);
    for(int i=0;i<14;i++) physical.Update(listener,0f,walker,[],[],[],1f/60f);
    var footstepVoices=(AudioVoice[][])typeof(PhysicalSounds).GetField("stepVoices",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(physical)!;
    if(!footstepVoices.SelectMany(voices=>voices).Any(voice=>voice.IsPlaying))
        throw new Exception("Moving did not start a footstep voice");
    physical.Update(listener,0f,walker,[],[],[new CubeImpactEvent { X=10f,Y=0.5f,Z=10f,Strength=1f }],1f/60f);
    int woodImpactCount=physical.ImpactPlayCount;
    physical.Update(listener,0f,walker,[],[],[new CubeImpactEvent { X=10f,Y=0.5f,Z=10f,Strength=1f,Material=PhysicsMaterial.Metal }],1f/60f);
    if(physical.ImpactPlayCount<=woodImpactCount)
        throw new Exception("A metal object's collision did not play the original impact sound");
    var impactVoices=(AudioVoice[][])typeof(PhysicalSounds).GetField("impactVoices",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(physical)!;
    if(impactVoices.Length!=5)
        throw new Exception("The original five cube impact variations were not restored");
    if(!impactVoices.SelectMany(voices=>voices).Any(voice=>voice.IsPlaying))
        throw new Exception("Collision did not start a cube impact voice");
    if(audio.Al.GetError()!=Silk.NET.OpenAL.AudioError.NoError)
        throw new Exception("OpenAL rejected a physical sound effect");
}
using(var audio=new AudioSystem())
using(var physical=new PhysicalSounds(audio))
{
    var ear=new System.Numerics.Vector3(10f,1.68f,10f);
    var idle=new FootstepSource(0,ear,0f,0f,0f);
    physical.Update(ear,0f,idle,[],[new CubeSoundSource(9,new System.Numerics.Vector3(10f,1f,10f),
        new System.Numerics.Vector3(0f,-3f,0f),0.4f,false,false,
        System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f)],[],1f/60f);
    physical.Update(ear,0f,idle,[],[new CubeSoundSource(9,new System.Numerics.Vector3(10f,0.4f,10f),
        System.Numerics.Vector3.Zero,0.4f,true,false,
        System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f)],[],1f/60f);
    if(physical.ImpactPlayCount!=1)
        throw new Exception("Cube landing without an impact packet did not play a hit");
}
{
    var rotation=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,MathF.PI/4f);
    var angular=2f*System.Numerics.Vector3.UnitZ;
    var pivot=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(-0.5f,-0.5f,0f),rotation);
    var tippingVelocity=-System.Numerics.Vector3.Cross(angular,pivot);
    // The server measures this slip speed; the client trusts it for online cubes.
    float tippingSlip=PhysicsBodies.ContactSlipSpeed(tippingVelocity,angular,rotation,0.5f);
    var tipping=new CubeSoundSource(1,System.Numerics.Vector3.Zero,tippingVelocity,0.5f,true,false,rotation,angular,tippingSlip);
    if(tipping.Velocity.Length()<1f || tippingSlip>0.05f)
        throw new Exception("A cube pivoting on an edge was mistaken for sliding");
    float slidingSlip=PhysicsBodies.ContactSlipSpeed(new System.Numerics.Vector3(2f,0f,0f),
        System.Numerics.Vector3.Zero,System.Numerics.Quaternion.Identity,0.5f);
    if(MathF.Abs(slidingSlip-2f)>0.01f)
        throw new Exception("A sliding cube did not produce floor contact motion");
    using var audio=new AudioSystem();
    using var physical=new PhysicalSounds(audio);
    var ear=new System.Numerics.Vector3(0f,1.5f,0f);
    var idle=new FootstepSource(0,ear,0f,0f,0f);
    physical.Update(ear,0f,idle,[],[tipping],[],1f/60f);
    var scrapes=(LoopingSound[])typeof(PhysicalSounds).GetField("scrapes",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(physical)!;
    if(scrapes.Any(scrape=>scrape.TargetVolume>0.001))
        throw new Exception("Pivoting on an edge started the scrape loop");
    // Online path: the client has no local velocity, only the server's slip speed.
    var serverSliding=new CubeSoundSource(1,System.Numerics.Vector3.Zero,System.Numerics.Vector3.Zero,0.5f,true,false,
        System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,slidingSlip);
    physical.Update(ear,0f,idle,[],[serverSliding],[],1f/60f);
    if(!scrapes.Any(scrape=>scrape.TargetVolume>0.01))
        throw new Exception("The server-measured slip speed did not start the online scrape loop");
}
{
    // A cube tipping from an edge onto a face must click on each corner that lands.
    using var audio=new AudioSystem();
    using var physical=new PhysicalSounds(audio);
    var ear=new System.Numerics.Vector3(0f,1.5f,0f);
    var idle=new FootstepSource(0,ear,0f,0f,0f);
    const float half=0.4f;
    int firstHits=physical.VertexHitCount;
    for(int i=0;i<=24;i++)
    {
        float theta=MathF.PI/4f*(1f-i/24f);
        var rotation=System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ,theta);
        var position=new System.Numerics.Vector3(0f,half*(MathF.Sin(theta)+MathF.Cos(theta)),0f);
        var tipping=new CubeSoundSource(1,position,new System.Numerics.Vector3(0f,-1.4f,0f),half,true,false,
            rotation,new System.Numerics.Vector3(0f,0f,-1.2f),0f);
        physical.Update(ear,0f,idle,[],[tipping],[],1f/60f);
    }
    if(physical.VertexHitCount-firstHits<2)
        throw new Exception("A cube tipping from an edge onto a face did not produce multiple corner hits");
    int restingHits=physical.VertexHitCount;
    for(int i=0;i<60;i++)
    {
        var resting=new CubeSoundSource(2,new System.Numerics.Vector3(0f,half,0f),System.Numerics.Vector3.Zero,
            half,true,false,System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f);
        physical.Update(ear,0f,idle,[],[resting],[],1f/60f);
    }
    if(physical.VertexHitCount!=restingHits)
        throw new Exception("A resting cube produced corner hit sounds");
}
using(var physics=new PhysicsBodies()) { }
using(var physics=new PhysicsBodies())
{
    var eye=new System.Numerics.Vector3(10f,1.68f,10f);
    var forward=System.Numerics.Vector3.UnitZ;
    if(!physics.Spawn(eye,forward,false,[],eye) ||
       !physics.TryPickup(eye,forward,System.Numerics.Quaternion.Identity,[]))
        throw new Exception("Could not pick up a cube for grab control check");
    float before=physics.Held!.HoldTarget.Z;
    if(!physics.NudgeGrabDistance(0,0.4f))
        throw new Exception("Mouse wheel did not adjust the active grab");
    for(int i=0;i<30;i++) physics.Update(1f/120f,eye,forward,
        System.Numerics.Quaternion.Identity,[],eye);
    if(physics.Held!.HoldTarget.Z<before+0.2f)
        throw new Exception("Mouse wheel did not move the grab target farther away");
    physics.RotateYaw=0.7f;
    for(int i=0;i<45;i++) physics.Update(1f/120f,eye,forward,
        System.Numerics.Quaternion.Identity,[],eye);
    if(MathF.Abs(physics.Held!.Rotation.Y)<0.03f)
        throw new Exception("Right mouse rotation did not turn the held cube");
}
using(var physics=new PhysicsBodies())
{
    var first=new SandboxDevice { Id=1,Kind=SandboxKind.Button,X=10f,Y=.7f,Z=10f,Qw=1f };
    var second=new SandboxDevice { Id=2,Kind=SandboxKind.Thruster,X=10.9f,Y=.7f,Z=10f,Qw=1f };
    if(!physics.SpawnSandboxDevice(first,[],System.Numerics.Vector3.Zero) ||
       !physics.SpawnSandboxDevice(second,[],System.Numerics.Vector3.Zero))
        throw new Exception("Sandbox devices did not create physical bodies");
    var eye=new System.Numerics.Vector3(10f,.7f,7f);
    if(!physics.TryPickup(eye,System.Numerics.Vector3.UnitZ,System.Numerics.Quaternion.Identity,[],1) ||
       physics.HeldCubeId(1)!=first.BodyId)
        throw new Exception("Sandbox device could not be picked up");
    physics.Release(1);
    if(!physics.TryWeld(first.BodyId,second.BodyId))
        throw new Exception("Nearby grabbable sandbox bodies could not be welded");
    for(int i=0;i<30;i++) physics.Update(1f/120f,eye,System.Numerics.Vector3.UnitZ,
        System.Numerics.Quaternion.Identity,[],System.Numerics.Vector3.Zero);
    physics.SyncSandboxDevices([first,second]);
    if(System.Numerics.Vector3.Distance(first.Position,second.Position)>.98f)
        throw new Exception("Sandbox weld did not preserve the body relationship");
}
using(var physics=new PhysicsBodies())
{
    if(!physics.SpawnCornerPlank(new System.Numerics.Vector3(10f,.14f,10f),0f,[],System.Numerics.Vector3.Zero) ||
       !physics.SpawnCornerPlank(new System.Numerics.Vector3(12.05f,.14f,10f),0f,[],System.Numerics.Vector3.Zero))
        throw new Exception("Pallet weld test could not place pallets");
    RigidCube[] pallets=physics.Bodies.Where(body=>body.Plank).ToArray();
    if(!physics.TryWeld(pallets[0].Id,pallets[1].Id))
        throw new Exception("Nearby wooden pallets could not be welded");
}
using(var physics=new PhysicsBodies())
{
    if(!physics.SpawnCornerPlank(new System.Numerics.Vector3(10f,.14f,10f),0f,[],System.Numerics.Vector3.Zero))
        throw new Exception("Could not create a pallet for the step check");
    if(!physics.CollidesPlayer(10f,10f,.3f) || physics.CollidesPlayer(10f,10f,.3f,.25f))
        throw new Exception("Pallet should block at floor height and clear at its top");
    var cubeEye=new System.Numerics.Vector3(13f,1.68f,10f);
    if(!physics.Spawn(cubeEye,System.Numerics.Vector3.UnitZ,false,[],cubeEye) ||
       !physics.CollidesPlayer(13f,12.35f,.3f,.25f))
        throw new Exception("A cube must still block a player standing at pallet height");
}
{
    var movement=new PlayerMotion();
    var settings=new DebugMenu();
    var camera=new System.Numerics.Vector3(10f,1.68f,10f);
    bool climbed=false;
    for(int i=0;i<300;i++)
    {
        movement.Update(1f/120f,new System.Numerics.Vector2(0f,1f),false,false,0f,0f,
            settings,ref camera,
            (x,z,feet)=>(z>=11f && z<12.8f && feet<.23f) || z>=13f,
            (x,z)=>(z>=11f && z<12.8f) ? .25f : 0f,true);
        climbed|=movement.FootElevation>.2f;
    }
    if(!climbed || camera.Z<12.7f || camera.Z>=13f)
        throw new Exception("Autostep did not cross a pallet or walked through a cube");
}
using(var physics=new PhysicsBodies())
{
    // A powered piston uses its facing direction to push the nearest free body.
    var piston=new SandboxDevice { Id=1,Kind=SandboxKind.Piston,X=10f,Y=.36f,Z=10f,Qw=1f };
    if(!physics.SpawnSandboxDevice(piston,[],System.Numerics.Vector3.Zero))
        throw new Exception("Sandbox piston did not create a physical body");
    var eye=new System.Numerics.Vector3(10f,.8f,9.4f);
    if(!physics.Spawn(eye,System.Numerics.Vector3.UnitZ,false,[],eye))
        throw new Exception("Could not spawn a cube in front of the piston");
    RigidCube cube=physics.Bodies.Single(body=>body.SandboxDeviceId==0);
    float before=cube.Velocity.Z;
    physics.ApplyPiston(piston.Position,piston.Rotation,1f/60f,piston.Id);
    physics.Update(1f/120f,eye,System.Numerics.Vector3.UnitZ,System.Numerics.Quaternion.Identity,[],eye);
    if(cube.Velocity.Z<=before+0.1f)
        throw new Exception("Powered piston did not push the cube in front of it");
}
{
    var sandbox=new SandboxWorld();
    sandbox.Devices.AddRange([
        new SandboxDevice { Id=1,Kind=SandboxKind.Button,X=10f,Y=.36f,Z=10f },
        new SandboxDevice { Id=2,Kind=SandboxKind.Lever,X=11f,Y=.36f,Z=10f },
        new SandboxDevice { Id=3,Kind=SandboxKind.And,X=12f,Y=.36f,Z=10f },
        new SandboxDevice { Id=4,Kind=SandboxKind.Lamp,X=13f,Y=.36f,Z=10f },
        new SandboxDevice { Id=5,Kind=SandboxKind.Thruster,X=14f,Y=.36f,Z=10f }
    ]);
    if(!sandbox.Connect(1,3)||!sandbox.Connect(2,3)||!sandbox.Connect(3,4)||!sandbox.Connect(3,5))
        throw new Exception("Sandbox logic wires could not be connected");
    sandbox.Interact(1);sandbox.Interact(2);
    int thrusts=0;
    sandbox.Update(.01f,_=>thrusts++);
    if(!sandbox.Devices.Single(d=>d.Id==3).State || !sandbox.Devices.Single(d=>d.Id==4).State || thrusts!=1)
        throw new Exception("Sandbox AND logic did not power its lamp and thruster");
    sandbox.Update(.3f);
    if(sandbox.Devices.Single(d=>d.Id==3).State)
        throw new Exception("Sandbox button did not release after its pulse");
}
{
    // XOR/NAND/XNOR parity gates, the edge-triggered Toggle, and the Timer clock.
    var sandbox=new SandboxWorld();
    sandbox.Devices.AddRange([
        new SandboxDevice { Id=1,Kind=SandboxKind.Button,X=10f,Y=.36f,Z=10f },
        new SandboxDevice { Id=2,Kind=SandboxKind.Lever,X=11f,Y=.36f,Z=10f },
        new SandboxDevice { Id=3,Kind=SandboxKind.Xor,X=12f,Y=.36f,Z=10f },
        new SandboxDevice { Id=4,Kind=SandboxKind.Nand,X=13f,Y=.36f,Z=10f },
        new SandboxDevice { Id=5,Kind=SandboxKind.Xnor,X=14f,Y=.36f,Z=10f },
        new SandboxDevice { Id=6,Kind=SandboxKind.Toggle,X=15f,Y=.36f,Z=10f },
        new SandboxDevice { Id=7,Kind=SandboxKind.Timer,X=16f,Y=.36f,Z=10f }
    ]);
    if(!sandbox.Connect(1,3)||!sandbox.Connect(2,3)||!sandbox.Connect(1,4)||!sandbox.Connect(2,4)||
       !sandbox.Connect(1,5)||!sandbox.Connect(2,5)||!sandbox.Connect(2,6))
        throw new Exception("New sandbox gate wires could not be connected");
    sandbox.Update(.01f);
    // Both inputs off: XOR false, NAND true (not-all), XNOR true (even parity).
    if(sandbox.Devices.Single(d=>d.Id==3).State || !sandbox.Devices.Single(d=>d.Id==4).State ||
       !sandbox.Devices.Single(d=>d.Id==5).State)
        throw new Exception("Sandbox XOR/NAND/XNOR initial parity was wrong");
    sandbox.Interact(2);
    sandbox.Update(.01f);
    // Lever only on: XOR true (odd), NAND true, XNOR false.
    if(!sandbox.Devices.Single(d=>d.Id==3).State || !sandbox.Devices.Single(d=>d.Id==4).State ||
       sandbox.Devices.Single(d=>d.Id==5).State)
        throw new Exception("Sandbox XOR/NAND/XNOR parity missed a single active input");
    sandbox.Interact(1);
    sandbox.Update(.01f);
    // Both on: XOR false (even), NAND false (all on), XNOR true.
    if(sandbox.Devices.Single(d=>d.Id==3).State || sandbox.Devices.Single(d=>d.Id==4).State ||
       !sandbox.Devices.Single(d=>d.Id==5).State)
        throw new Exception("Sandbox XOR/NAND/XNOR parity missed two active inputs");
    // Toggle latches on the lever's rising edge only.
    sandbox.Update(.01f);
    if(!sandbox.Devices.Single(d=>d.Id==6).State)
        throw new Exception("Sandbox toggle did not latch on the rising edge");
    sandbox.Update(.01f);
    if(!sandbox.Devices.Single(d=>d.Id==6).State)
        throw new Exception("Sandbox toggle flipped without a rising edge");
    sandbox.Interact(2);
    sandbox.Update(.01f);
    if(!sandbox.Devices.Single(d=>d.Id==6).State)
        throw new Exception("Sandbox toggle dropped its latch on a falling edge");
    sandbox.Interact(2);sandbox.Interact(1);
    sandbox.Update(.01f);
    if(sandbox.Devices.Single(d=>d.Id==6).State)
        throw new Exception("Sandbox toggle did not flip on the second rising edge");
    // The timer clock free-runs and blinks at 0.5s.
    var timer=sandbox.Devices.Single(d=>d.Id==7);
    sandbox.Update(.25f);
    if(timer.State) throw new Exception("Sandbox timer fired too early");
    sandbox.Update(.3f);
    if(!timer.State) throw new Exception("Sandbox timer did not toggle after 0.5s");
    sandbox.Update(.5f);
    if(timer.State) throw new Exception("Sandbox timer did not toggle back");
}
using(var acoustics=new VercidiumAcoustics([new RectangleF(8f,8f,4f,1f)]))
{
    AcousticSource[] sources=
        [new(AcousticSourceIds.Monster,new System.Numerics.Vector3(12f,1.7f,12f)),
         new(AcousticSourceIds.Lamp(new System.Numerics.Vector2(9f,7f)),
             new System.Numerics.Vector3(9f,3f,7f))];
    var ear=new System.Numerics.Vector3(7f,1.7f,7f);
    for(int i=0;i<100 && (!acoustics.HasRaytracedRoom ||
        !acoustics.HasSourceResult(AcousticSourceIds.Monster) ||
        acoustics.GroupFor(AcousticSourceIds.Monster)<0);i++)
    { acoustics.Update(ear,sources);System.Threading.Thread.Sleep(10); }
    if(!acoustics.HasRaytracedRoom) throw new Exception("Vercidium did not return room reverb");
    if(!acoustics.HasSourceResult(AcousticSourceIds.Monster))
        throw new Exception("Vercidium did not raytrace the monster source");
    if(acoustics.GroupFor(AcousticSourceIds.Monster)<0)
        throw new Exception("Vercidium did not assign the monster to a grouped EAX room");
    using var audio=new AudioSystem();
    audio.Reverb!.SetRoom(acoustics.Room);
    audio.Reverb.SetGroups(acoustics.Groups);
    using var clip=audio.Load("monster.wav");
    using var voice=new AudioVoice(clip);
    voice.SetReverbGroup(acoustics.GroupFor(AcousticSourceIds.Monster));
    if(audio.Al.GetError()!=Silk.NET.OpenAL.AudioError.NoError)
        throw new Exception("OpenAL rejected the grouped EAX reverb send");
}
using(var gameAudio=new GameAudio())
{
    if(!(bool)typeof(GameAudio).GetField("available",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(gameAudio)!)
        throw new Exception("Game audio did not initialize");
    gameAudio.SetWorld([new RectangleF(8f,8f,4f,1f)]);
    gameAudio.Update(new System.Numerics.Vector3(7f,1.7f,7f),0f,[new System.Numerics.Vector2(9f,7f)],true,
        new MonsterState { X=12f,Y=1.7f,Z=12f },true,0f,0f,0f,1f/60f,[],[],[],[]);
    gameAudio.Update(new System.Numerics.Vector3(7f,1.7f,7f),0f,
        [new System.Numerics.Vector2(8f,7f),new System.Numerics.Vector2(15f,7f)],true,
        null,false,0f,0f,0f,1f/60f,[],[],[],[]);
    var gameSystem=(AudioSystem)typeof(GameAudio).GetField("system",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(gameAudio)!;
    if(gameSystem.Al.GetError()!=Silk.NET.OpenAL.AudioError.NoError)
        throw new Exception("OpenAL rejected the Vercidium room reverb");
    var lampVoices=(LoopingSound[])typeof(GameAudio).GetField("lamps",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(gameAudio)!;
    if(lampVoices.Count(lamp=>lamp.TargetVolume>0.001)<2)
        throw new Exception("Nearby ceiling fixtures did not have separate hum sources");
}
using(var physics=new PhysicsBodies())
{
    var eye=new System.Numerics.Vector3(10f,1.68f,10f);
    if(!physics.Spawn(eye,System.Numerics.Vector3.UnitZ,false,[],eye))
        throw new Exception("Could not spawn cube for sound check");
    for(int i=0;i<180;i++) physics.Update(1f/120f,eye,System.Numerics.Vector3.UnitZ,
        System.Numerics.Quaternion.Identity,[],eye);
    if(physics.DrainCubeImpacts().Length==0)
        throw new Exception("Cube landing did not produce a concrete impact event");
}
using(var physics=new PhysicsBodies())
{
    var forward=System.Numerics.Vector3.UnitZ;
    var firstEye=new System.Numerics.Vector3(10f,1.68f,10f);
    var secondEye=new System.Numerics.Vector3(14f,1.68f,10f);
    if(!physics.Spawn(firstEye,forward,false,[],firstEye) ||
       !physics.Spawn(secondEye,forward,false,[],secondEye) ||
       !physics.TryPickup(firstEye,forward,System.Numerics.Quaternion.Identity,[],1) ||
       !physics.TryPickup(secondEye,forward,System.Numerics.Quaternion.Identity,[],2) ||
       physics.HolderOf(physics.Bodies[0])!=1 || physics.HolderOf(physics.Bodies[1])!=2)
        throw new Exception("Two players could not hold separate free cubes");
    physics.Release(1);
    if(!physics.TryPickup(firstEye,forward,System.Numerics.Quaternion.Identity,[],3) ||
       physics.HolderOf(physics.Bodies[0])!=3)
        throw new Exception("Another player could not take a released cube");
}
using(var physics=new PhysicsBodies())
using(var audio=new AudioSystem())
using(var physical=new PhysicalSounds(audio))
{
    // A held cube carried through open air must stay silent: no scrape loop, no
    // landing click, and no click even if a stale impact packet names its id.
    var eye=new System.Numerics.Vector3(10f,1.68f,10f);
    var forward=System.Numerics.Vector3.UnitZ;
    if(!physics.Spawn(eye,forward,false,[],eye))
        throw new Exception("Could not spawn cube for carried-sound check");
    if(!physics.TryPickup(eye,forward,System.Numerics.Quaternion.Identity,[],1))
        throw new Exception("Could not hold cube for carried-sound check");
    var scrapes=(LoopingSound[])typeof(PhysicalSounds).GetField("scrapes",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(physical)!;
    int hits=physical.ImpactPlayCount;
    for(int i=0;i<120;i++)
    {
        eye+=forward*0.02f;
        physics.Update(1f/120f,eye,forward,System.Numerics.Quaternion.Identity,[],eye);
        RigidCube body=physics.Bodies[0];
        physical.Update(eye,0f,new FootstepSource(0,eye,0f,0f,0f),[],
            [new CubeSoundSource(body.Id,body.Position,body.Velocity,body.HalfSize,
                body.Grounded,body.HasHoldTarget,body.Rotation,body.AngularVelocity,
                PhysicsBodies.ContactSlipSpeed(body))],
            physics.DrainCubeImpacts(),
            1f/120f);
    }
    if(physical.ImpactPlayCount!=hits)
        throw new Exception("A held cube in open air still played a hit sound");
    if(scrapes.Any(scrape=>scrape.TargetVolume>0.001))
        throw new Exception("A held cube in open air still played a scrape loop");
    // A held cube must also ignore a stale impact packet that names it, and never
    // scrape even with a high server-measured slip speed while it touches nothing.
    int staleHits=physical.ImpactPlayCount;
    physical.Update(eye,0f,new FootstepSource(0,eye,0f,0f,0f),[],
        [new CubeSoundSource(999,new System.Numerics.Vector3(10f,0.5f,10f),
            System.Numerics.Vector3.Zero,0.4f,true,true,
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,3f)],
        [new CubeImpactEvent { CubeId=999,X=10f,Y=0.5f,Z=10f,Strength=1f }],1f/120f);
    if(physical.ImpactPlayCount!=staleHits)
        throw new Exception("A held cube played a stale impact packet");
    if(scrapes.Any(scrape=>scrape.TargetVolume>0.001))
        throw new Exception("A held cube scraped without touching a surface");
    // But a held cube that is actually banged or dragged along a surface must sound,
    // for both the local simulation and the server-driven online path.
    var ear=new System.Numerics.Vector3(10f,1.7f,10f);
    int surfaceHits=physical.ImpactPlayCount;
    // Frame 1: still in the air, moving fast, resting on nothing.
    physical.Update(ear,0f,new FootstepSource(0,ear,0f,0f,0f),[],
        [new CubeSoundSource(700,new System.Numerics.Vector3(10f,1.4f,10f),
            new System.Numerics.Vector3(4f,0f,0f),0.4f,false,true,
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f)],[],1f/60f);
    if(physical.ImpactPlayCount!=surfaceHits)
        throw new Exception("A held cube in the air played a hit sound before touching");
    // Frame 2: banged down onto the floor, so a corner newly lands hard.
    physical.Update(ear,0f,new FootstepSource(0,ear,0f,0f,0f),[],
        [new CubeSoundSource(700,new System.Numerics.Vector3(10f,0.4f,10f),
            new System.Numerics.Vector3(2f,-3f,0f),0.4f,true,true,
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f)],[],1f/60f);
    if(physical.ImpactPlayCount==surfaceHits)
        throw new Exception("A held cube banged against the floor did not play a hit sound");
    // Frame 3: dragged sideways while still touching the floor, so it must scrape.
    physical.Update(ear,0f,new FootstepSource(0,ear,0f,0f,0f),[],
        [new CubeSoundSource(700,new System.Numerics.Vector3(10.06f,0.4f,10f),
            new System.Numerics.Vector3(3f,0f,0f),0.4f,true,true,
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f)],[],1f/60f);
    if(!scrapes.Any(scrape=>scrape.TargetVolume>0.001))
        throw new Exception("A held cube dragged along the floor did not scrape");
    // Lifting it back into the air again must silence the scrape.
    physical.Update(ear,0f,new FootstepSource(0,ear,0f,0f,0f),[],
        [new CubeSoundSource(700,new System.Numerics.Vector3(10f,1.4f,10f),
            new System.Numerics.Vector3(3f,1f,0f),0.4f,false,true,
            System.Numerics.Quaternion.Identity,System.Numerics.Vector3.Zero,0f)],[],1f/60f);
    if(scrapes.Any(scrape=>scrape.TargetVolume>0.001))
        throw new Exception("A held cube kept scraping after it left the floor");
}
using(var game=new GameWindow())
{
    var key=typeof(GameWindow).GetMethod("ProcessCmdKey",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var menuField=typeof(GameWindow).GetField("menu",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var gameMenu=(DebugMenu)menuField.GetValue(game)!;
    if(gameMenu.Open) throw new Exception("Settings menu should start closed");
    key.Invoke(game,[new Message(),Keys.Tab]);
    if(!gameMenu.Open) throw new Exception("Tab did not open settings menu");
    key.Invoke(game,[new Message(),Keys.Tab]);
    if(gameMenu.Open) throw new Exception("Tab did not close settings menu");
    // Solo sandbox: enable locally, then spawn a device through the sandbox browser path.
    var sandboxField=typeof(GameWindow).GetField("localSandbox",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var localSandbox=(SandboxWorld)sandboxField.GetValue(game)!;
    var physicsField=typeof(GameWindow).GetField("physics",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var localPhysics=(PhysicsBodies)physicsField.GetValue(game)!;
    gameMenu.SandboxEnabled=true;
    var spawn=typeof(GameWindow).GetMethod("SpawnSandboxItem",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    spawn.Invoke(game,[SandboxKind.Lamp]);
    if(localSandbox.Devices.Count!=1 || localPhysics.Bodies.All(body=>body.SandboxDeviceId==0))
        throw new Exception("Solo sandbox spawn did not create a device body");
    // Typing "b" into a connection field must not open the sandbox browser.
    var keyDownField=typeof(SilkForm).GetField("KeyDown",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    var keyDown=(EventHandler<KeyEventArgs>)keyDownField.GetValue(game)!;
    gameMenu.Open=true;gameMenu.EditingJoinKey=true;
    keyDown(game,new KeyEventArgs(Keys.B,false));
    if(gameMenu.Open && gameMenu.SandboxPage)
        throw new Exception("Typing B in a connection field opened the sandbox page");
    gameMenu.FinishEditing();gameMenu.Open=false;
    // O opens a private Ponder screen, consumes movement/menu navigation, and Escape closes it.
    keyDown(game,new KeyEventArgs(Keys.O,false));
    if(!gameMenu.Open || !gameMenu.PonderPage) throw new Exception("O did not open the Ponder screen");
    int ponderStep=gameMenu.PonderStep;
    var ponderMessage=new Message();
    key.Invoke(game,[ponderMessage,Keys.Right]);
    if(gameMenu.PonderStep==(ponderStep)) throw new Exception("Ponder navigation was captured by the settings menu");
    keyDown(game,new KeyEventArgs(Keys.Escape,false));
    if(gameMenu.Open || gameMenu.PonderPage) throw new Exception("Escape did not close the Ponder screen");
    // ... but pressing B during gameplay still opens it (solo needs no lobby).
    keyDown(game,new KeyEventArgs(Keys.B,false));
    if(!gameMenu.SandboxPage)
        throw new Exception("B did not open the sandbox browser in solo gameplay");
    // Sandbox mode OFF must keep the browser closed, in solo and online alike.
    gameMenu.ToggleSandbox();
    gameMenu.SandboxEnabled=false;
    keyDown(game,new KeyEventArgs(Keys.B,false));
    if(gameMenu.SandboxPage)
        throw new Exception("B opened the sandbox browser while sandbox mode was off");
    // The browser now follows the settings-menu list design and keyboard routing.
    gameMenu.SandboxEnabled=true;
    gameMenu.ToggleSandbox();
    if(!gameMenu.SandboxPage) throw new Exception("The sandbox browser did not open");
    gameMenu.CycleSandboxCategory(1);
    if(gameMenu.SandboxCategory!=1 || gameMenu.SandboxSelected!=0)
        throw new Exception("Sandbox category change did not reset the selection");
    gameMenu.SelectSandbox(99);
    if(gameMenu.SandboxSelected!=gameMenu.SandboxItemCount-1)
        throw new Exception("Sandbox selection did not clamp to the category");
    gameMenu.CycleSandboxCategory(2);
    if(gameMenu.SandboxCategory!=2 || gameMenu.SandboxScroll!=0)
        throw new Exception("Sandbox LOGIC category did not reset the scroll");
    gameMenu.SelectSandbox(9);
    if(gameMenu.SandboxSelected!=9 || gameMenu.SandboxScroll!=3)
        throw new Exception("Sandbox selection did not keep the cursor in view");
    gameMenu.SelectSandbox(-9);
    if(gameMenu.SandboxSelected!=0 || gameMenu.SandboxScroll!=0)
        throw new Exception("Sandbox selection did not scroll back to the top");
    gameMenu.SelectSandbox(gameMenu.SandboxItemCount);
    gameMenu.MouseWheel(-120);
    if(gameMenu.SandboxScroll!=gameMenu.SandboxMaxScroll)
        throw new Exception("Sandbox mouse wheel scrolled past the end");
    gameMenu.MouseWheel(120);
    if(gameMenu.SandboxScroll!=Math.Max(0,gameMenu.SandboxMaxScroll-1))
        throw new Exception("Sandbox mouse wheel did not scroll the list");
    for(int i=0;i<gameMenu.SandboxMaxScroll+2;i++) gameMenu.MouseWheel(120);
    if(gameMenu.SandboxScroll!=0)
        throw new Exception("Sandbox mouse wheel did not reach the top");
    gameMenu.MouseWheel(-120);
    if(gameMenu.SandboxScroll!=Math.Min(1,gameMenu.SandboxMaxScroll))
        throw new Exception("Sandbox mouse wheel did not scroll down from the top");
    int settingsRow=gameMenu.Selected;
    key.Invoke(game,[new Message(),Keys.Down]);
    if(gameMenu.Selected!=settingsRow)
        throw new Exception("Sandbox arrow keys changed the settings selection");
    using(var sandboxImage=gameMenu.Draw())
        if(sandboxImage.Width!=320 || sandboxImage.Height!=240 ||
           !sandboxImage.Pixels.Where((_,i)=>i%4==3).Any(alpha=>alpha!=0))
            throw new Exception("The sandbox browser did not render");
    gameMenu.ToggleSandbox();
    gameMenu.SandboxEnabled=false;
}
if(!Beanrooms.Program.TryOnlineEndpoint("https://beanrooms-game.onrender.com","test-key",out string endpoint)
    || endpoint!="wss://beanrooms-game.onrender.com/ws?key=test-key")
    throw new Exception("Online server address format changed");
Console.WriteLine("PASS: menu, Tab toggle, textures, concrete sound playback, corner impact events, online scrape slip, Vercidium, multiplayer grabs, solo sandbox, online address");
if(args.Contains("--network") || args.Contains("--websocket"))
{
    bool webSocket=args.Contains("--websocket");
    using var client=webSocket
        ? new LocalNetClient("ws://127.0.0.1:10000/ws?key=port-test-key",10000)
        : new LocalNetClient("127.0.0.1",27075);
    client.Start();
    var deadline=DateTime.UtcNow.AddSeconds(8);
    while(!client.Connected && DateTime.UtcNow<deadline) Thread.Sleep(25);
    NetMessage? welcome=client.ConsumeWorld();
    if(!client.Connected || client.Id<=0 || welcome?.Type!="welcome")
        throw new Exception("Local multiplayer handshake failed: "+client.Status);
    float finalX=welcome.X+0.25f;
    for(int i=0;i<100;i++)
        client.Send(new NetMessage { Type="state",X=welcome.X+i*0.0025f,Y=welcome.Y,
            Z=welcome.Z,RespawnId=welcome.RespawnId,HoldDistance=2.25f });
    client.Send(new NetMessage { Type="state",X=finalX,Y=welcome.Y,Z=welcome.Z,
        RespawnId=welcome.RespawnId,HoldDistance=2.25f });
    deadline=DateTime.UtcNow.AddSeconds(5);
    while(DateTime.UtcNow<deadline &&
          MathF.Abs((client.Snapshot().Players.FirstOrDefault(p=>p.Id==client.Id)?.X??0f)-finalX)>0.02f)
        Thread.Sleep(25);
    if(MathF.Abs((client.Snapshot().Players.FirstOrDefault(p=>p.Id==client.Id)?.X??0f)-finalX)>0.02f)
        throw new Exception("Latest player state did not reach the server");
    bool sandboxWasEnabled=client.Snapshot().SandboxEnabled;
    client.Send(new NetMessage { Type="sandbox_toggle" });
    deadline=DateTime.UtcNow.AddSeconds(3);
    while(client.Snapshot().SandboxEnabled==sandboxWasEnabled && DateTime.UtcNow<deadline) Thread.Sleep(25);
    if(client.Snapshot().SandboxEnabled==sandboxWasEnabled)
        throw new Exception("Host sandbox toggle did not round-trip");
    if(!client.Snapshot().SandboxEnabled)
    {
        client.Send(new NetMessage { Type="sandbox_toggle" });
        deadline=DateTime.UtcNow.AddSeconds(3);
        while(!client.Snapshot().SandboxEnabled && DateTime.UtcNow<deadline) Thread.Sleep(25);
    }
    if(!client.Snapshot().SandboxEnabled)
        throw new Exception("Host could not enable sandbox mode");
    client.Send(new NetMessage { Type="device_spawn",Action=(int)SandboxKind.Button });
    client.Send(new NetMessage { Type="device_spawn",Action=(int)SandboxKind.Lamp });
    deadline=DateTime.UtcNow.AddSeconds(3);
    while(client.Snapshot().Devices.Length<2 && DateTime.UtcNow<deadline) Thread.Sleep(25);
    var devices=client.Snapshot().Devices;
    if(devices.Length<2) throw new Exception("Sandbox device spawn did not replicate");
    if(devices.Any(device=>device.BodyId<=0))
        throw new Exception("Sandbox device did not receive a physical body");
    int button=devices.First(d=>d.Kind==SandboxKind.Button).Id;
    int lamp=devices.First(d=>d.Kind==SandboxKind.Lamp).Id;
    client.Send(new NetMessage { Type="weld",FromId=devices[0].BodyId,ToId=devices[1].BodyId });
    client.Send(new NetMessage { Type="wire",FromId=button,ToId=lamp });
    client.Send(new NetMessage { Type="device_interact",Action=button });
    deadline=DateTime.UtcNow.AddSeconds(2);
    while(!client.Snapshot().Devices.Any(d=>d.Id==lamp&&d.State) && DateTime.UtcNow<deadline) Thread.Sleep(25);
    if(!client.Snapshot().Devices.Any(d=>d.Id==lamp&&d.State))
        throw new Exception("Sandbox wire state did not replicate");
    Console.WriteLine($"PASS: {(webSocket?"WebSocket":"local TCP")} multiplayer welcome, sandbox toggle, and world state");
}
if(args.Contains("--netmenu"))
{
    using var host=new LocalNetClient("127.0.0.1",27075);host.Start();
    using var guest=new LocalNetClient("127.0.0.1",27075);guest.Start();
    var deadline=DateTime.UtcNow.AddSeconds(8);
    while((!host.Connected||!guest.Connected) && DateTime.UtcNow<deadline) Thread.Sleep(25);
    if(!host.Connected||!guest.Connected) throw new Exception("Session-menu test: handshake failed");
    if(!host.SupportsSandbox || !guest.SupportsSandbox)
        throw new Exception("Session-menu test: current server did not advertise sandbox support");
    deadline=DateTime.UtcNow.AddSeconds(3);
    while((host.Snapshot().HostId==0||guest.Snapshot().HostId==0) && DateTime.UtcNow<deadline) Thread.Sleep(25);
    LocalNetClient menuHost=host.Id<guest.Id?host:guest;
    LocalNetClient menuGuest=menuHost==host?guest:host;
    if(menuHost.Snapshot().HostId!=menuHost.Id || menuGuest.Snapshot().HostId==menuGuest.Id)
        throw new Exception("Session-menu test: snapshots did not identify the server host");
    if(menuHost.Snapshot().SandboxEnabled)
    {
        menuHost.Send(new NetMessage { Type="sandbox_toggle" });
        deadline=DateTime.UtcNow.AddSeconds(3);
        while(menuHost.Snapshot().SandboxEnabled && DateTime.UtcNow<deadline) Thread.Sleep(25);
        if(menuHost.Snapshot().SandboxEnabled) throw new Exception("Session-menu test: could not reset sandbox state");
    }
    // Drive the real session-menu path. Snapshot synchronization must establish
    // host status; the test intentionally never assigns SandboxHost.
    var window=new GameWindow();
    var networkField=typeof(GameWindow).GetField("network",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    networkField.SetValue(window,menuHost);
    var windowMenu=(DebugMenu)typeof(GameWindow).GetField("menu",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
    var update=typeof(GameWindow).GetMethod("UpdateGame",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
    windowMenu.SandboxHost=false;
    deadline=DateTime.UtcNow.AddSeconds(2);
    while(!windowMenu.SandboxHost && DateTime.UtcNow<deadline)
    { update.Invoke(window,[1f/60f]);Thread.Sleep(25); }
    if(!windowMenu.SandboxHost || windowMenu.NetworkHost!=menuHost.Id)
        throw new Exception("Session-menu test: host status did not come from snapshots");
    windowMenu.Open=true;windowMenu.SetPage(9);windowMenu.Selected=7;
    windowMenu.AdjustSelected(1);
    deadline=DateTime.UtcNow.AddSeconds(3);
    while(!menuHost.Snapshot().SandboxEnabled && DateTime.UtcNow<deadline) Thread.Sleep(25);
    if(!menuHost.Snapshot().SandboxEnabled)
        throw new Exception("Host session-menu toggle did not enable sandbox");
    if(!menuGuest.Snapshot().SandboxEnabled)
        throw new Exception("Sandbox mode did not replicate to the guest");

    var guestWindow=new GameWindow();
    networkField.SetValue(guestWindow,menuGuest);
    var guestMenu=(DebugMenu)typeof(GameWindow).GetField("menu",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(guestWindow)!;
    deadline=DateTime.UtcNow.AddSeconds(2);
    while(guestMenu.SandboxHost && DateTime.UtcNow<deadline)
    { update.Invoke(guestWindow,[1f/60f]);Thread.Sleep(25); }
    if(guestMenu.SandboxHost) throw new Exception("Session-menu test: guest host status did not come from snapshots");
    guestMenu.Open=true;guestMenu.SetPage(9);guestMenu.Selected=7;
    guestMenu.AdjustSelected(1);
    Thread.Sleep(250);
    if(!menuHost.Snapshot().SandboxEnabled || !menuGuest.Snapshot().SandboxEnabled)
        throw new Exception("Server accepted a guest sandbox toggle");

    networkField.SetValue(window,new LocalNetClient());
    window.Dispose();
    networkField.SetValue(guestWindow,new LocalNetClient());
    guestWindow.Dispose();
    Console.WriteLine("PASS: server-authorized session-menu sandbox toggle reached host and guest");
}
if(args.Contains("--photon"))
{
    string room="PORT-"+Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    using var first=new PhotonNetClient(room);
    using var second=new PhotonNetClient(room);
    first.Start();second.Start();
    var deadline=DateTime.UtcNow.AddSeconds(20);
    while((!first.Connected||!second.Connected) && DateTime.UtcNow<deadline)
    { first.Service(1f/60f);second.Service(1f/60f);Thread.Sleep(16); }
    if(!first.Connected||!second.Connected)
        throw new Exception($"Photon room join failed: {first.Status}, {second.Status}");
    PhotonNetClient host=first.Id<second.Id?first:second;
    PhotonNetClient guest=host==first?second:first;
    deadline=DateTime.UtcNow.AddSeconds(5);
    while((host.Snapshot().HostId!=host.Id||guest.Snapshot().HostId!=host.Id) && DateTime.UtcNow<deadline)
    { first.Service(1f/60f);second.Service(1f/60f);Thread.Sleep(16); }
    if(host.Snapshot().HostId!=host.Id||guest.Snapshot().HostId!=host.Id)
        throw new Exception("Photon snapshots did not select the room master as host");
    if(!host.Snapshot().Cubes.Any(cube=>cube.Plank)||!guest.Snapshot().Cubes.Any(cube=>cube.Plank))
        throw new Exception("Photon corner planks did not replicate to every room player");
    host.Send(new NetMessage { Type="sandbox_toggle" });
    deadline=DateTime.UtcNow.AddSeconds(5);
    while(!guest.Snapshot().SandboxEnabled && DateTime.UtcNow<deadline)
    { first.Service(1f/60f);second.Service(1f/60f);Thread.Sleep(16); }
    if(!host.Snapshot().SandboxEnabled||!guest.Snapshot().SandboxEnabled)
        throw new Exception("Photon master sandbox toggle did not replicate");
    guest.Send(new NetMessage { Type="sandbox_toggle" });
    for(int i=0;i<60;i++) { first.Service(1f/60f);second.Service(1f/60f);Thread.Sleep(16); }
    if(!host.Snapshot().SandboxEnabled||!guest.Snapshot().SandboxEnabled)
        throw new Exception("Photon room master accepted a guest sandbox toggle");
    int cubesBefore=host.Snapshot().Cubes.Length;
    PlayerState spawn=host.Snapshot().Players.First(player=>player.Id==host.Id);
    // Try each cardinal direction from the authoritative player's clear spawn;
    // one direction is guaranteed to have space in a generated room.
    foreach(float copyYaw in new[]{0f,MathF.PI*.5f,MathF.PI,MathF.PI*1.5f})
    {
        host.Send(new NetMessage { Type="state",X=spawn.X,Y=spawn.Y,Z=spawn.Z,Yaw=copyYaw,Pitch=0f });
        host.Send(new NetMessage { Type="copy_cube",Action=0,R=.18f,G=.42f,B=.73f });
        for(int i=0;i<12;i++) { first.Service(1f/60f);second.Service(1f/60f);Thread.Sleep(16); }
        if(host.Snapshot().Cubes.Length>cubesBefore) break;
    }
    deadline=DateTime.UtcNow.AddSeconds(5);
    while(!guest.Snapshot().Cubes.Any(cube=>MathF.Abs(cube.R-.18f)<.01f&&MathF.Abs(cube.G-.42f)<.01f&&MathF.Abs(cube.B-.73f)<.01f)&&DateTime.UtcNow<deadline)
    { first.Service(1f/60f);second.Service(1f/60f);Thread.Sleep(16); }
    if(host.Snapshot().Cubes.Length<=cubesBefore || !guest.Snapshot().Cubes.Any(cube=>MathF.Abs(cube.R-.18f)<.01f&&MathF.Abs(cube.G-.42f)<.01f&&MathF.Abs(cube.B-.73f)<.01f))
        throw new Exception("Photon sandbox copy cube did not replicate");
    Console.WriteLine("PASS: Photon Realtime room, authoritative snapshots, sandbox permissions, and copied cubes");
}
