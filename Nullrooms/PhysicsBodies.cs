using System.Drawing;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Beanrooms;

internal sealed class RigidCube
{
    internal int Id;
    internal int SandboxDeviceId;
    internal Vector3 Position, Velocity, AngularVelocity;
    internal Vector3 Color = new(0.82f, 0.69f, 0.22f);
    internal Quaternion Rotation = Quaternion.Identity;
    internal Vector3 HoldTarget, HoldTargetVelocity;
    internal Quaternion CarryRotationOffset = Quaternion.Identity;
    internal float HalfSize, Drag = 0.025f, RestDrag;
    // Cubes leave this empty; pallets use their actual low rectangular bounds.
    internal Vector3 LocalHalfExtents;
    internal bool HasHoldTarget, Grounded, Emissive, Plank;
    internal PhysicsMaterial Material=PhysicsMaterial.Wood;
    internal float ImpactCooldown;
    internal BodyHandle Handle;
}

internal readonly record struct GrabInput(Vector3 Eye,Vector3 Forward,Quaternion Rotation,
    float HoldDistance,float Frequency,float Damping,float Smoothing,float RotateYaw=0f,float RotatePitch=0f,
    bool SnapHold=false,float SnapStep=.25f);

internal struct PhysicsNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public SpringSettings ContactSpringiness;
    public float MaximumRecoveryVelocity, FrictionCoefficient;

    public PhysicsNarrowPhaseCallbacks(SpringSettings spring, float maximumRecoveryVelocity, float frictionCoefficient)
    {
        ContactSpringiness = spring;
        MaximumRecoveryVelocity = maximumRecoveryVelocity;
        FrictionCoefficient = frictionCoefficient;
    }

    public void Initialize(Simulation simulation) { }
    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;
    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial.FrictionCoefficient = FrictionCoefficient;
        pairMaterial.MaximumRecoveryVelocity = MaximumRecoveryVelocity;
        pairMaterial.SpringSettings = ContactSpringiness;
        return true;
    }

    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold) => true;
    public void Dispose() { }
}

internal struct PhysicsPoseCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    public float LinearDamping, AngularDamping;
    Vector<float> linearDampingDt, angularDampingDt;
    Vector3Wide gravityWideDt;

    public PhysicsPoseCallbacks(Vector3 gravity, float linearDamping, float angularDamping)
    {
        Gravity = gravity;
        LinearDamping = linearDamping;
        AngularDamping = angularDamping;
        linearDampingDt = default;
        angularDampingDt = default;
        gravityWideDt = default;
    }

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;
    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        linearDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - LinearDamping, 0, 1), dt));
        angularDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - AngularDamping, 0, 1), dt));
        gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear = (velocity.Linear + gravityWideDt) * linearDampingDt;
        velocity.Angular = velocity.Angular * angularDampingDt;
    }
}

internal sealed class PhysicsBodies : IDisposable
{
    internal readonly List<RigidCube> Bodies = new();
    readonly List<CubeImpactEvent> cubeImpacts = new();
    internal CubeImpactEvent[] DrainCubeImpacts()
    {
        CubeImpactEvent[] events=cubeImpacts.ToArray();
        cubeImpacts.Clear();
        return events;
    }
    sealed class Grab
    {
        internal required RigidCube Cube;
        internal Vector3 LocalPoint;
        internal float DistanceOffset;
        internal float LastHoldDistance;
        internal Quaternion TargetOrientation;
        internal ConstraintHandle Linear, Angular;
        internal bool Active;
    }
    readonly Dictionary<int,Grab> grabs = new();
    readonly List<ConstraintHandle> welds = new();
    readonly HashSet<(int First,int Second)> weldedPairs = new();
    sealed class CableLink
    {
        internal required BodyHandle[] Segments;
        internal required float HalfLength;
    }
    readonly List<CableLink> cables = new();
    internal static int CableSegmentCount(float distance) => Math.Clamp((int)MathF.Ceiling(distance/0.8f),3,18);
    float previewReach;
    internal RigidCube? Held => grabs.TryGetValue(0,out Grab? grab) ? grab.Cube : null;
    internal int HolderOf(RigidCube cube) => grabs.FirstOrDefault(pair=>pair.Value.Cube==cube).Key;
    internal bool HeldBy(int holderId) => grabs.ContainsKey(holderId);
    internal int HeldCubeId(int holderId) => grabs.TryGetValue(holderId,out Grab? grab) ? grab.Cube.Id : 0;
    internal bool NudgeGrabDistance(int holderId,float delta)
    {
        if(!grabs.TryGetValue(holderId,out Grab? grab)) return false;
        float desired=Math.Clamp(grab.LastHoldDistance+grab.DistanceOffset+delta,0.35f,6f);
        grab.DistanceOffset=desired-grab.LastHoldDistance;
        return true;
    }
    internal void ApplyThruster(Vector3 position,float yaw,float dt,int sourceDeviceId=0)
    {
        RigidCube? cube=Bodies.Where(body=>!body.HasHoldTarget && body.SandboxDeviceId!=sourceDeviceId)
            .OrderBy(body=>Vector3.DistanceSquared(body.Position,position)).FirstOrDefault();
        if(cube==null || Vector3.DistanceSquared(cube.Position,position)>1.5f*1.5f) return;
        BodyReference reference=simulation.Bodies[cube.Handle];
        reference.Velocity.Linear+=new Vector3(MathF.Sin(yaw),0f,MathF.Cos(yaw))*10f*dt;
    }
    internal float PickupRange = 5f, HoldDistance = 2.25f, Frequency = 8.5f, Damping = 1.35f, TargetSmoothing = 18f, SlideResistance = 4.5f;
    internal float RotateYaw=0f,RotatePitch=0f;
    internal bool SnapHold=false;
    internal float SnapStep=.25f;

    const float Step = 1f / 120f;
    const float Ceiling = 3.4f;
    const float RoomMin = 4f, RoomMax = 188f;
    readonly BufferPool pool = new();
    Simulation simulation = null!;
    double accumulator;
    bool environmentReady;
    int nextBodyId = 1;

    internal PhysicsBodies() => ResetSimulation();

    void ResetSimulation()
    {
        if (simulation != null) simulation.Dispose();
        pool.Clear();
        simulation = Simulation.Create(pool,
            new PhysicsNarrowPhaseCallbacks(new SpringSettings(30f, 1.4f), 1.5f, 1.55f),
            new PhysicsPoseCallbacks(new Vector3(0, -9.81f, 0), 0.025f, 0.025f),
            new SolveDescription(8, 2, 128), null, null);
        environmentReady = false;
        grabs.Clear();
        welds.Clear();
        weldedPairs.Clear();
        cables.Clear();
        previewReach=0f;
        accumulator = 0;
    }

    internal void Clear()
    {
        Bodies.Clear();
        cubeImpacts.Clear();
        ResetSimulation();
    }

    public void Dispose()
    {
        simulation?.Dispose();
        pool.Clear();
    }

    void EnsureEnvironment(IReadOnlyList<RectangleF> walls)
    {
        if (environmentReady) return;
        AddStaticBox(96f, -0.05f, 96f, 192f, 0.1f, 192f);
        AddStaticBox(96f, Ceiling + 0.05f, 96f, 192f, 0.1f, 192f);
        foreach (RectangleF wall in walls)
            AddStaticBox((wall.Left + wall.Right) * 0.5f, Ceiling * 0.5f,
                (wall.Top + wall.Bottom) * 0.5f, MathF.Max(wall.Width, 0.05f), Ceiling, MathF.Max(wall.Height, 0.05f));
        environmentReady = true;
    }

    void AddStaticBox(float x, float y, float z, float width, float height, float length)
    {
        var shape = new Box(width, height, length);
        TypedIndex shapeIndex = simulation.Shapes.Add(in shape);
        simulation.Statics.Add(new StaticDescription(new RigidPose(new Vector3(x, y, z), Quaternion.Identity), shapeIndex));
    }

    internal static Vector3 LocalExtents(RigidCube body)
        => body.LocalHalfExtents.LengthSquared()>0.000001f ? body.LocalHalfExtents : new Vector3(body.HalfSize);

    internal static Vector3 Extents(RigidCube body)
        => RotatedExtents(LocalExtents(body),body.Rotation);

    internal static Vector3 RotatedExtents(float half,Quaternion rotation)
        => RotatedExtents(new Vector3(half),rotation);

    internal static Vector3 RotatedExtents(Vector3 localExtents,Quaternion rotation)
    {
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(rotation);
        return new Vector3(
            localExtents.X*MathF.Abs(m.M11) + localExtents.Y*MathF.Abs(m.M21) + localExtents.Z*MathF.Abs(m.M31),
            localExtents.X*MathF.Abs(m.M12) + localExtents.Y*MathF.Abs(m.M22) + localExtents.Z*MathF.Abs(m.M32),
            localExtents.X*MathF.Abs(m.M13) + localExtents.Y*MathF.Abs(m.M23) + localExtents.Z*MathF.Abs(m.M33));
    }

    // A powered piston shoves the nearest free body directly in front of its face
    // along the device's 3D forward axis, Minecraft-style (with the base excluded).
    internal void ApplyPiston(Vector3 position,Quaternion rotation,float dt,int sourceDeviceId=0)
    {
        Vector3 forward=Vector3.Transform(Vector3.UnitZ,rotation);
        if(forward.LengthSquared()<0.5f) forward=Vector3.UnitZ;
        forward=Vector3.Normalize(forward);
        Vector3 face=position+forward*0.5f;
        RigidCube? cube=null;
        float nearestAlong=float.MaxValue;
        foreach(RigidCube body in Bodies)
        {
            if(body.HasHoldTarget || body.SandboxDeviceId==sourceDeviceId) continue;
            Vector3 delta=body.Position-face;
            float along=Vector3.Dot(delta,forward);
            if(along<0.08f || along>1.6f || along>=nearestAlong) continue;
            Vector3 radial=delta-forward*along;
            if(radial.LengthSquared()>0.6f*0.6f) continue;
            cube=body;nearestAlong=along;
        }
        if(cube==null) return;
        simulation.Bodies[cube.Handle].Velocity.Linear+=forward*11f*dt;
    }
    internal void ApplyMechanism(SandboxDevice device,float dt)
    {
        RigidCube? source=Bodies.FirstOrDefault(body=>body.SandboxDeviceId==device.Id);
        Vector3 forward=Vector3.Transform(Vector3.UnitZ,device.Rotation);
        if(forward.LengthSquared()<.5f) forward=Vector3.UnitZ;
        forward=Vector3.Normalize(forward);
        if(device.Kind==SandboxKind.Hinge && source!=null)
        { simulation.Bodies[source.Handle].Velocity.Angular+=Vector3.UnitY*5f*dt;return; }
        if(device.Kind==SandboxKind.Wheel && source!=null)
        { simulation.Bodies[source.Handle].Velocity.Linear+=forward*8f*dt;return; }
        if(device.Kind==SandboxKind.Rope)
        {
            RigidCube? target=Bodies.Where(body=>body!=source&&!body.HasHoldTarget)
                .OrderBy(body=>Vector3.DistanceSquared(body.Position,device.Position)).FirstOrDefault();
            if(target!=null)
            {
                Vector3 pull=device.Position-target.Position;
                if(pull.LengthSquared()<36f&&pull.LengthSquared()>.04f)
                    simulation.Bodies[target.Handle].Velocity.Linear+=Vector3.Normalize(pull)*7f*dt;
            }
            return;
        }
        if(device.Kind==SandboxKind.Conveyor)
            foreach(RigidCube body in Bodies)
            {
                if(body==source||body.HasHoldTarget) continue;
                Vector3 delta=body.Position-device.Position;
                if(MathF.Abs(delta.X)<.8f&&MathF.Abs(delta.Z)<.8f&&delta.Y>.05f&&delta.Y<.85f)
                    simulation.Bodies[body.Handle].Velocity.Linear+=forward*5f*dt;
            }
    }

    internal bool DevicePressed(SandboxDevice device)
    {
        foreach(RigidCube body in Bodies)
        {
            if(body.SandboxDeviceId==device.Id) continue;
            Vector3 delta=body.Position-device.Position;
            if(delta.X*delta.X+delta.Z*delta.Z<.48f*.48f && MathF.Abs(delta.Y)<.55f) return true;
        }
        return false;
    }
    internal static float ContactSlipSpeed(Vector3 velocity,Vector3 angularVelocity,Quaternion rotation,float halfSize)
        => ContactSlipSpeed(velocity,angularVelocity,rotation,new Vector3(halfSize));

    internal static float ContactSlipSpeed(Vector3 velocity,Vector3 angularVelocity,Quaternion rotation,Vector3 localExtents)
    {
        float lengthSquared=rotation.LengthSquared();
        Quaternion normalized=lengthSquared<0.5f?Quaternion.Identity:
            MathF.Abs(lengthSquared-1f)>0.001f?Quaternion.Normalize(rotation):rotation;
        Vector3 xAxis=Vector3.Transform(Vector3.UnitX*localExtents.X,normalized);
        Vector3 yAxis=Vector3.Transform(Vector3.UnitY*localExtents.Y,normalized);
        Vector3 zAxis=Vector3.Transform(Vector3.UnitZ*localExtents.Z,normalized);
        Vector3[] radii=new Vector3[8];
        float minY=float.MaxValue;
        int index=0;
        for(int x=-1;x<=1;x+=2)
        for(int y=-1;y<=1;y+=2)
        for(int z=-1;z<=1;z+=2)
        {
            Vector3 radius=x*xAxis+y*yAxis+z*zAxis;
            radii[index++]=radius;
            minY=MathF.Min(minY,radius.Y);
        }
        float squaredSlip=0f;
        int contacts=0;
        for(int i=0;i<radii.Length;i++)
        {
            Vector3 radius=radii[i];
            if(radius.Y>minY+0.02f) continue;
            Vector3 contactVelocity=velocity+Vector3.Cross(angularVelocity,radius);
            squaredSlip+=contactVelocity.X*contactVelocity.X+contactVelocity.Z*contactVelocity.Z;
            contacts++;
        }
        return contacts>0?MathF.Sqrt(squaredSlip/contacts):0f;
    }

    internal static float ContactSlipSpeed(RigidCube body)
        => ContactSlipSpeed(body.Velocity,body.AngularVelocity,body.Rotation,LocalExtents(body));

    static bool Overlap(Vector3 a, Vector3 ae, Vector3 b, Vector3 be) =>
        MathF.Abs(a.X - b.X) < ae.X + be.X && MathF.Abs(a.Y - b.Y) < ae.Y + be.Y && MathF.Abs(a.Z - b.Z) < ae.Z + be.Z;

    bool SpawnBlocked(Vector3 center, float half, IReadOnlyList<RectangleF> walls, Vector3 player)
        => SpawnBlocked(center,new Vector3(half),walls,player);

    bool SpawnBlocked(Vector3 center, Vector3 extents, IReadOnlyList<RectangleF> walls, Vector3 player)
    {
        if (center.X - extents.X < RoomMin || center.X + extents.X > RoomMax ||
            center.Z - extents.Z < RoomMin || center.Z + extents.Z > RoomMax ||
            center.Y - extents.Y < 0.01f || center.Y + extents.Y > Ceiling - 0.01f) return true;
        foreach (RectangleF wall in walls)
            if (center.X + extents.X > wall.Left && center.X - extents.X < wall.Right &&
                center.Z + extents.Z > wall.Top && center.Z - extents.Z < wall.Bottom) return true;
        foreach (RigidCube other in Bodies)
            if (Overlap(center, extents, other.Position, Extents(other))) return true;
        return MathF.Abs(center.X - player.X) < extents.X + 0.3f &&
               MathF.Abs(center.Z - player.Z) < extents.Z + 0.3f && center.Y - extents.Y < 1.9f;
    }

    internal bool Spawn(Vector3 eye, Vector3 forward, bool emissive, IReadOnlyList<RectangleF> walls, Vector3 player)
        => Spawn(eye, forward, emissive, walls, player, emissive ? new Vector3(0.2f, 0.72f, 1f) : new Vector3(0.82f, 0.69f, 0.22f));

    internal bool Spawn(Vector3 eye, Vector3 forward, bool emissive, IReadOnlyList<RectangleF> walls, Vector3 player, Vector3 color,
        PhysicsMaterial material=PhysicsMaterial.Wood)
    {
        EnsureEnvironment(walls);
        float half = emissive ? 0.38f : 0.43f;
        Vector3 position = eye + forward * 2.35f;
        position.Y = Math.Clamp(position.Y, half + 0.08f, Ceiling - half - 0.08f);
        if (SpawnBlocked(position, half, walls, player)) return false;

        var shape = new Box(half * 2, half * 2, half * 2);
        TypedIndex shapeIndex = simulation.Shapes.Add(in shape);
        BodyInertia inertia = shape.ComputeInertia(1f);
        BodyDescription description = BodyDescription.CreateDynamic(
            new RigidPose(position, Quaternion.Identity), new BodyVelocity(), inertia,
            new CollidableDescription(shapeIndex, 0f, 3f, ContinuousDetection.Continuous()),
            new BodyActivityDescription(0.0001f, 60));
        BodyHandle handle = simulation.Bodies.Add(description);
        Bodies.Add(new RigidCube { Id = nextBodyId++, Position = position, HalfSize = half, Emissive = emissive,
            Material=material, Color = color, Handle = handle });
        return true;
    }

    internal bool SpawnCornerPlank(Vector3 position,float yaw,IReadOnlyList<RectangleF> walls,Vector3 player)
    {
        EnsureEnvironment(walls);
        // A low square pallet: several visible boards share one solid Bepu body.
        const float half=.90f, halfHeight=.11f;
        position.Y=halfHeight+.03f;
        if(SpawnBlocked(position,new Vector3(half,halfHeight,half),walls,player)) return false;
        Quaternion rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
        var shape=new Box(half*2f,halfHeight*2f,half*2f);
        TypedIndex shapeIndex=simulation.Shapes.Add(in shape);
        BodyInertia inertia=shape.ComputeInertia(.7f);
        BodyHandle handle=simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position,rotation),new BodyVelocity(),inertia,
            new CollidableDescription(shapeIndex,0f,3f,ContinuousDetection.Continuous()),
            new BodyActivityDescription(.0001f,60)));
        Bodies.Add(new RigidCube { Id=nextBodyId++,Position=position,HalfSize=half,
            LocalHalfExtents=new Vector3(half,halfHeight,half),Plank=true,
            Rotation=rotation,Material=PhysicsMaterial.Wood,Color=new Vector3(.43f,.24f,.10f),Handle=handle });
        return true;
    }

    internal bool SpawnSandboxDevice(SandboxDevice device,IReadOnlyList<RectangleF> walls,Vector3 player)
    {
        EnsureEnvironment(walls);
        const float half=0.35f;
        Vector3 position=device.Position;
        if(SpawnBlocked(position,half,walls,player)) return false;
        Quaternion rotation=device.Rotation;
        if(rotation.LengthSquared()<0.001f) rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,device.Yaw);
        var shape=new Box(half*2f,half*2f,half*2f);
        TypedIndex shapeIndex=simulation.Shapes.Add(in shape);
        BodyInertia inertia=shape.ComputeInertia(0.8f);
        BodyHandle handle=simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position,rotation),new BodyVelocity(),inertia,
            new CollidableDescription(shapeIndex,0f,3f,ContinuousDetection.Continuous()),
            new BodyActivityDescription(0.0001f,60)));
        var body=new RigidCube { Id=nextBodyId++,SandboxDeviceId=device.Id,Position=position,
            HalfSize=half,Material=PhysicsMaterial.Metal,Color=new Vector3(.42f,.48f,.52f),Handle=handle };
        Bodies.Add(body);device.BodyId=body.Id;
        return true;
    }

    internal void SyncSandboxDevices(IReadOnlyList<SandboxDevice> devices)
    {
        foreach(SandboxDevice device in devices)
        {
            RigidCube? body=Bodies.FirstOrDefault(candidate=>candidate.SandboxDeviceId==device.Id);
            if(body==null) continue;
            device.BodyId=body.Id;device.X=body.Position.X;device.Y=body.Position.Y;device.Z=body.Position.Z;
            device.Qx=body.Rotation.X;device.Qy=body.Rotation.Y;device.Qz=body.Rotation.Z;device.Qw=body.Rotation.W;
            device.Yaw=MathF.Atan2(2f*(body.Rotation.W*body.Rotation.Y+body.Rotation.X*body.Rotation.Z),
                1f-2f*(body.Rotation.Y*body.Rotation.Y+body.Rotation.Z*body.Rotation.Z));
        }
    }

    internal bool TryWeld(int firstId,int secondId)
    {
        if(firstId==secondId) return false;
        (int First,int Second) pair=firstId<secondId?(firstId,secondId):(secondId,firstId);
        if(!weldedPairs.Add(pair)) return false;
        RigidCube? first=Bodies.FirstOrDefault(body=>body.Id==firstId);
        RigidCube? second=Bodies.FirstOrDefault(body=>body.Id==secondId);
        if(first==null||second==null||first.HasHoldTarget||second.HasHoldTarget)
        { weldedPairs.Remove(pair);return false; }
        // Measure reach from each body's true rotated shape, so two wide pallets
        // can be welded from their opposite boards instead of being limited by a cube-only centre radius.
        Vector3 reach=Extents(first)+Extents(second)+new Vector3(.35f);
        Vector3 separation=Vector3.Abs(first.Position-second.Position);
        if(separation.X>reach.X||separation.Y>reach.Y||separation.Z>reach.Z)
        { weldedPairs.Remove(pair);return false; }
        Quaternion inverse=Quaternion.Inverse(first.Rotation);
        var weld=new Weld
        {
            LocalOffset=Vector3.Transform(second.Position-first.Position,inverse),
            LocalOrientation=Quaternion.Normalize(inverse*second.Rotation),
            SpringSettings=new SpringSettings(40f,2.2f)
        };
        welds.Add(simulation.Solver.Add(first.Handle,second.Handle,weld));
        return true;
    }

    internal static Quaternion CableRotation(Vector3 direction)
    {
        direction=Vector3.Normalize(direction);
        float dot=Vector3.Dot(Vector3.UnitY,direction);
        if(dot<-.9999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX,MathF.PI);
        Quaternion result=new(Vector3.Cross(Vector3.UnitY,direction),1f+dot);
        return Quaternion.Normalize(result);
    }

    internal bool AttachCable(SandboxWire wire,SandboxDevice from,SandboxDevice to)
    {
        RigidCube? first=Bodies.FirstOrDefault(body=>body.SandboxDeviceId==from.Id);
        RigidCube? last=Bodies.FirstOrDefault(body=>body.SandboxDeviceId==to.Id);
        if(first==null||last==null||first.Handle==last.Handle) return false;
        BodyReference a=simulation.Bodies[first.Handle],b=simulation.Bodies[last.Handle];
        Vector3 start=a.Pose.Position+Vector3.Transform(Vector3.UnitY*.22f,a.Pose.Orientation);
        Vector3 end=b.Pose.Position+Vector3.Transform(Vector3.UnitY*.22f,b.Pose.Orientation);
        Vector3 delta=end-start;
        float distance=delta.Length();
        if(distance<.05f||distance>20f) return false;
        int count=wire.Segments>0?wire.Segments:CableSegmentCount(distance);
        float span=distance/count;
        float length=span*1.08f;
        float half=length*.5f;
        Quaternion rotation=CableRotation(delta);
        var shape=new Box(.055f,length,.055f);
        TypedIndex shapeIndex=simulation.Shapes.Add(in shape);
        BodyInertia inertia=shape.ComputeInertia(.035f);
        var handles=new BodyHandle[count];
        var spring=new SpringSettings(16f,1.6f);
        BodyHandle previous=first.Handle;
        for(int index=0;index<count;index++)
        {
            Vector3 center=Vector3.Lerp(start,end,(index+.5f)/count);
            handles[index]=simulation.Bodies.Add(BodyDescription.CreateDynamic(
                new RigidPose(center,rotation),new BodyVelocity(),inertia,
                new CollidableDescription(shapeIndex,0f,1f,ContinuousDetection.Continuous()),
                new BodyActivityDescription(.0001f,60)));
            Vector3 localA=index==0
                ?Vector3.Transform(start-a.Pose.Position,Quaternion.Inverse(a.Pose.Orientation))
                :new Vector3(0f,half,0f);
            simulation.Solver.Add(previous,handles[index],new BallSocket
            {
                LocalOffsetA=localA,LocalOffsetB=new Vector3(0f,-half,0f),SpringSettings=spring
            });
            previous=handles[index];
        }
        Vector3 localB=Vector3.Transform(end-b.Pose.Position,Quaternion.Inverse(b.Pose.Orientation));
        simulation.Solver.Add(previous,last.Handle,new BallSocket
        {
            LocalOffsetA=new Vector3(0f,half,0f),LocalOffsetB=localB,SpringSettings=spring
        });
        cables.Add(new CableLink { Segments=handles,HalfLength=half });
        return true;
    }

    internal CableSegmentState[] CableSegments()
    {
        var states=new List<CableSegmentState>();
        foreach(CableLink cable in cables)
            foreach(BodyHandle handle in cable.Segments)
            {
                RigidPose pose=simulation.Bodies[handle].Pose;
                Vector3 axis=Vector3.Transform(Vector3.UnitY*cable.HalfLength,pose.Orientation);
                Vector3 a=pose.Position-axis,b=pose.Position+axis;
                states.Add(new CableSegmentState { Ax=a.X,Ay=a.Y,Az=a.Z,Bx=b.X,By=b.Y,Bz=b.Z });
            }
        return states.ToArray();
    }

    // A separate PhysicsBodies instance is used for the local placement preview,
    // so its cable cannot pull an authoritative device before the second click.
    internal void BeginCablePreview(Vector3 start,Vector3 target,IReadOnlyList<RectangleF> walls)
    {
        Clear();
        EnsureEnvironment(walls);
        previewReach=MathF.Max(1.5f,Vector3.Distance(start,target)*1.12f);
        AddPreviewAnchor(1,start);
        AddPreviewAnchor(2,target);
        var wire=new SandboxWire { From=1,To=2,Segments=CableSegmentCount(previewReach) };
        var from=new SandboxDevice { Id=1,X=start.X,Y=start.Y,Z=start.Z };
        var to=new SandboxDevice { Id=2,X=target.X,Y=target.Y,Z=target.Z };
        AttachCable(wire,from,to);
    }

    void AddPreviewAnchor(int id,Vector3 position)
    {
        var shape=new Box(.06f,.06f,.06f);
        TypedIndex shapeIndex=simulation.Shapes.Add(in shape);
        BodyInertia inertia=shape.ComputeInertia(1000f);
        BodyHandle handle=simulation.Bodies.Add(BodyDescription.CreateDynamic(
            new RigidPose(position,Quaternion.Identity),new BodyVelocity(),inertia,
            new CollidableDescription(shapeIndex,0f,0f),new BodyActivityDescription(.0001f,60)));
        Bodies.Add(new RigidCube { Id=-id,SandboxDeviceId=id,Position=position,HalfSize=.03f,Handle=handle });
    }

    internal void StepCablePreview(float dt,Vector3 start,Vector3 target,IReadOnlyList<RectangleF> walls)
    {
        if(Bodies.Count<2 || cables.Count==0) BeginCablePreview(start,target,walls);
        else if(Vector3.Distance(start,target)>previewReach*1.12f) BeginCablePreview(start,target,walls);
        if(Bodies.Count<2) return;
        BodyReference a=simulation.Bodies[Bodies[0].Handle],b=simulation.Bodies[Bodies[1].Handle];
        a.Pose.Position=start;b.Pose.Position=target;
        a.Velocity.Linear=Vector3.Zero;b.Velocity.Linear=Vector3.Zero;
        a.Velocity.Angular=Vector3.Zero;b.Velocity.Angular=Vector3.Zero;
        accumulator=Math.Min(accumulator+Math.Clamp(dt,0f,.05f),.1f);
        while(accumulator>=Step)
        {
            simulation.Timestep(Step,null);
            accumulator-=Step;
        }
    }

    internal static float RayBox(Vector3 origin, Vector3 direction, Vector3 center, Vector3 extents)
    {
        float entry = 0f, exit = 1000f;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            float c = axis == 0 ? center.X : axis == 1 ? center.Y : center.Z;
            float e = axis == 0 ? extents.X : axis == 1 ? extents.Y : extents.Z;
            if (MathF.Abs(d) < 1e-6f) { if (o < c - e || o > c + e) return float.PositiveInfinity; continue; }
            float a = (c - e - o) / d, b = (c + e - o) / d;
            if (a > b) (a, b) = (b, a);
            entry = MathF.Max(entry, a); exit = MathF.Min(exit, b);
            if (entry > exit) return float.PositiveInfinity;
        }
        return exit >= 0f ? entry : float.PositiveInfinity;
    }

    internal static float WallRay(Vector3 origin, Vector3 direction, IReadOnlyList<RectangleF> walls, float limit)
    {
        float nearest = limit;
        foreach (RectangleF wall in walls)
        {
            Vector3 center = new((wall.Left + wall.Right) * 0.5f, Ceiling * 0.5f, (wall.Top + wall.Bottom) * 0.5f);
            Vector3 extents = new(wall.Width * 0.5f, Ceiling * 0.5f, wall.Height * 0.5f);
            float t = RayBox(origin, direction, center, extents);
            if (t >= 0f && t < nearest) nearest = t;
        }
        return nearest;
    }

    internal bool TryPickup(Vector3 eye, Vector3 direction, Quaternion cameraRotation, IReadOnlyList<RectangleF> walls, int holderId=0)
    {
        if (grabs.ContainsKey(holderId)) return false;
        float nearest = MathF.Min(PickupRange, WallRay(eye, direction, walls, PickupRange));
        RigidCube? candidate = null;
        foreach (RigidCube body in Bodies)
        {
            if (grabs.Values.Any(grab=>grab.Cube==body)) continue;
            Quaternion inverse = Quaternion.Inverse(body.Rotation);
            float t = RayBox(Vector3.Transform(eye - body.Position, inverse), Vector3.Transform(direction, inverse), Vector3.Zero, LocalExtents(body));
            if (t >= 0f && t < nearest) { nearest = t; candidate = body; }
        }
        if (candidate == null) return false;

        candidate.RestDrag = candidate.Drag;
        candidate.Drag = 0.8f;
        Vector3 hit = eye + direction * nearest;
        Quaternion pickupOrientation = Quaternion.Normalize(simulation.Bodies[candidate.Handle].Pose.Orientation);
        var grab=new Grab { Cube=candidate,
            LocalPoint=Vector3.Transform(hit-candidate.Position,Quaternion.Inverse(pickupOrientation)),
            DistanceOffset=nearest-HoldDistance,LastHoldDistance=HoldDistance,TargetOrientation=pickupOrientation };
        grabs.Add(holderId,grab);
        candidate.HoldTarget = hit;
        candidate.HoldTargetVelocity = Vector3.Zero;
        candidate.CarryRotationOffset = Quaternion.Normalize(Quaternion.Inverse(cameraRotation) * pickupOrientation);
        AddGrabConstraints(grab,hit,Frequency,Damping);
        candidate.HasHoldTarget = true;
        return true;
    }

    void AddGrabConstraints(Grab grab,Vector3 target,float frequency,float damping)
    {
        RigidCube cube=grab.Cube;
        var linear = new OneBodyLinearServo
        {
            LocalOffset = grab.LocalPoint,
            Target = target,
            ServoSettings = new ServoSettings(float.MaxValue, 0f, 360f),
            SpringSettings = new SpringSettings(MathF.Max(0.1f, frequency), MathF.Max(0.05f, damping))
        };
        float side = MathF.Max(LocalExtents(cube).X,MathF.Max(LocalExtents(cube).Y,LocalExtents(cube).Z))*2f;
        float inertia = side * side / 6f;
        var angular = new OneBodyAngularServo
        {
            TargetOrientation = grab.TargetOrientation,
            ServoSettings = new ServoSettings(12f, 0f, MathF.Max(10f, inertia * 260f)),
            SpringSettings = new SpringSettings(MathF.Max(0.1f, frequency), MathF.Max(0.05f, damping))
        };
        if(!grab.Active)
        {
            grab.Linear=simulation.Solver.Add(cube.Handle,linear);
            grab.Angular=simulation.Solver.Add(cube.Handle,angular);
            grab.Active=true;
        }
        else
        {
            simulation.Solver.ApplyDescription(grab.Linear,linear);
            simulation.Solver.ApplyDescription(grab.Angular,angular);
        }
        simulation.Bodies[cube.Handle].Activity.TimestepsUnderThresholdCount = 0;
    }

    internal void Release(int holderId=0)
    {
        if (!grabs.Remove(holderId,out Grab? grab)) return;
        simulation.Solver.Remove(grab.Linear);
        simulation.Solver.Remove(grab.Angular);
        RigidCube released = grab.Cube;
        released.HasHoldTarget = false;
        released.Drag = released.RestDrag;
    }

    internal bool CollidesPlayer(float x, float z, float radius,float footHeight=0f)
    {
        foreach (RigidCube body in Bodies)
        {
            if (body.HasHoldTarget) continue;
            Vector3 e = Extents(body);
            if (body.Position.Y+e.Y<=footHeight+0.025f ||
                body.Position.Y-e.Y>=footHeight+2.1f) continue;
            if (MathF.Abs(body.Position.X - x) < e.X + radius && MathF.Abs(body.Position.Z - z) < e.Z + radius) return true;
        }
        return false;
    }

    internal void Update(float dt, Vector3 eye, Vector3 forward, Quaternion cameraRotation, IReadOnlyList<RectangleF> walls, Vector3 player,
        Func<int,GrabInput?>? onlineInput=null,IReadOnlyList<Vector3>? playerPositions=null)
    {
        EnsureEnvironment(walls);
        accumulator = Math.Min(accumulator + Math.Clamp(dt, 0f, 0.05f), 0.1);
        while (accumulator >= Step)
        {
            foreach(var pair in grabs.ToArray())
            {
                Grab grab=pair.Value;
                GrabInput? input=onlineInput==null
                    ? new GrabInput(eye,forward,cameraRotation,HoldDistance,Frequency,Damping,TargetSmoothing,RotateYaw,RotatePitch,SnapHold,SnapStep)
                    : onlineInput(pair.Key);
                if(input==null) { Release(pair.Key); continue; }
                GrabInput current=input.Value;
                RigidCube cube=grab.Cube;
                grab.LastHoldDistance=current.HoldDistance;
                Quaternion handRotation=Quaternion.CreateFromYawPitchRoll(current.RotateYaw,current.RotatePitch,0f);
                Quaternion desiredOrientation=Quaternion.Normalize(current.Rotation*handRotation*cube.CarryRotationOffset);
                Vector3 desiredTarget=current.Eye+current.Forward*MathF.Max(0f,current.HoldDistance+grab.DistanceOffset);
                Vector3 worldGrabOffset=Vector3.Transform(grab.LocalPoint,desiredOrientation);
                float halfHeight=RotatedExtents(LocalExtents(cube),desiredOrientation).Y;
                float centerY=Math.Clamp(desiredTarget.Y-worldGrabOffset.Y,halfHeight+0.005f,Ceiling-halfHeight-0.005f);
                desiredTarget.Y=centerY+worldGrabOffset.Y;
                Vector3 center=desiredTarget-worldGrabOffset;
                if(current.SnapHold)
                {
                    float step=Math.Clamp(current.SnapStep,.05f,2f);
                    center=new Vector3(MathF.Round(center.X/step)*step,MathF.Round(center.Y/step)*step,MathF.Round(center.Z/step)*step);
                }
                Vector3 extents=RotatedExtents(LocalExtents(cube),desiredOrientation);
                foreach(Vector3 playerPosition in playerPositions??[player])
                {
                    if(center.Y-extents.Y>2.15f) continue;
                    Vector2 away=new(center.X-playerPosition.X,center.Z-playerPosition.Z);
                    float clearance=MathF.Max(extents.X,extents.Z)+0.42f;
                    if(away.LengthSquared()>=clearance*clearance) continue;
                    Vector2 normal=away.LengthSquared()>0.0001f?Vector2.Normalize(away):
                        Vector2.Normalize(new Vector2(current.Forward.X,current.Forward.Z)+new Vector2(0.001f));
                    center.X=playerPosition.X+normal.X*clearance;
                    center.Z=playerPosition.Z+normal.Y*clearance;
                }
                desiredTarget=center+worldGrabOffset;
                Vector3 previous=cube.HoldTarget;
                float blend=1f-MathF.Exp(-MathF.Max(current.Smoothing,0.1f)*Step);
                cube.HoldTarget=Vector3.Lerp(cube.HoldTarget,desiredTarget,blend);
                cube.HoldTargetVelocity=(cube.HoldTarget-previous)/Step;
                grab.TargetOrientation=Quaternion.Normalize(Quaternion.Slerp(grab.TargetOrientation,desiredOrientation,blend));
                AddGrabConstraints(grab,cube.HoldTarget,current.Frequency,current.Damping);
            }
            Vector3[] before=Bodies.Select(body=>simulation.Bodies[body.Handle].Velocity.Linear).ToArray();
            simulation.Timestep(Step, null);
            for(int i=0;i<Bodies.Count;i++)
            {
                RigidCube body=Bodies[i];
                body.ImpactCooldown=MathF.Max(0f,body.ImpactCooldown-Step);
                if(body.HasHoldTarget || body.ImpactCooldown>0f || before[i].LengthSquared()<0.8f) continue;
                BodyReference reference=simulation.Bodies[body.Handle];
                Vector3 contactChange=reference.Velocity.Linear-before[i]-new Vector3(0f,-9.81f*Step,0f);
                float impulse=contactChange.Length();
                if(impulse<1.5f) continue;
                Vector3 point=reference.Pose.Position;
                cubeImpacts.Add(new CubeImpactEvent { CubeId=body.Id,X=point.X,Y=point.Y,Z=point.Z,
                    Strength=Math.Clamp((impulse-1f)/6f,0.15f,1f),Material=body.Material });
                body.ImpactCooldown=0.11f;
            }
            ApplyRestingResistance();
            accumulator -= Step;
        }
        SyncBodies();
    }

    void ApplyRestingResistance()
    {
        foreach (RigidCube body in Bodies)
        {
            if (body.HasHoldTarget) continue;
            BodyReference reference = simulation.Bodies[body.Handle];
            Vector3 position = reference.Pose.Position;
            Vector3 extents = Extents(body);
            if (position.Y - extents.Y > 0.035f) continue;
            Vector3 linear = reference.Velocity.Linear;
            float resistance = MathF.Exp(-MathF.Max(0f, SlideResistance) * Step);
            linear.X *= resistance;
            linear.Z *= resistance;
            if (MathF.Abs(linear.X) < 0.015f) linear.X = 0f;
            if (MathF.Abs(linear.Z) < 0.015f) linear.Z = 0f;
            reference.Velocity.Linear = linear;
        }
    }

    void SyncBodies()
    {
        foreach (RigidCube body in Bodies)
        {
            BodyReference reference = simulation.Bodies[body.Handle];
            body.Position = reference.Pose.Position;
            body.Rotation = Quaternion.Normalize(reference.Pose.Orientation);
            body.Velocity = reference.Velocity.Linear;
            body.AngularVelocity = reference.Velocity.Angular;
            body.Grounded = body.Position.Y - Extents(body).Y <= 0.025f;
        }
    }
}
