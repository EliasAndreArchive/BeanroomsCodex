using Beanrooms;
using StbImageSharp;
using System.Drawing;

var menu=new DebugMenu();
menu.ShowTitle();
using var image=menu.Draw();
if(image.Width!=320||image.Height!=240 || !image.Pixels.Where((_,i)=>i%4==3).Any(alpha=>alpha!=0))
    throw new Exception("The menu did not render into its 320x240 overlay");
menu.LeaveTitle();menu.Open=true;menu.NetworkConnected=true;menu.SandboxHost=true;menu.SetPage(9);menu.Selected=7;
int sandboxToggles=0;menu.SandboxToggle=()=>sandboxToggles++;
menu.AdjustSelected(1);
if(sandboxToggles!=1) throw new Exception("Host session menu did not expose sandbox mode");
menu.SandboxHost=false;menu.AdjustSelected(1);
if(sandboxToggles!=1) throw new Exception("Guest could change host sandbox mode");
menu.NetworkConnected=false;menu.AdjustSelected(1);
if(sandboxToggles!=2) throw new Exception("Solo session menu did not expose sandbox mode");
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
    var impactVoices=(AudioVoice[][])typeof(PhysicalSounds).GetField("impactVoices",
        System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(physical)!;
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
    // ... but pressing B during gameplay still opens it (solo needs no lobby).
    keyDown(game,new KeyEventArgs(Keys.B,false));
    if(!gameMenu.SandboxPage)
        throw new Exception("B did not open the sandbox browser in solo gameplay");
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
    if(!webSocket)
    {
        client.Send(new NetMessage { Type="sandbox_toggle" });
        deadline=DateTime.UtcNow.AddSeconds(3);
        while(!client.Snapshot().SandboxEnabled && DateTime.UtcNow<deadline) Thread.Sleep(25);
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
    }
    Console.WriteLine($"PASS: {(webSocket?"WebSocket":"local TCP")} multiplayer welcome and world state");
}
