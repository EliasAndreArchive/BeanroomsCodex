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
    internal Vector3 Position, Velocity, AngularVelocity;
    internal Vector3 Color = new(0.82f, 0.69f, 0.22f);
    internal Quaternion Rotation = Quaternion.Identity;
    internal Vector3 HoldTarget, HoldTargetVelocity;
    internal Quaternion CarryRotationOffset = Quaternion.Identity;
    internal float HalfSize, Drag = 0.025f, RestDrag;
    internal bool HasHoldTarget, Grounded, Emissive;
    internal BodyHandle Handle;
}

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
    internal RigidCube? Held { get; private set; }
    internal float PickupRange = 5f, HoldDistance = 2.25f, Frequency = 8.5f, Damping = 1.35f, TargetSmoothing = 18f, SlideResistance = 4.5f;

    const float Step = 1f / 120f;
    const float Ceiling = 3.4f;
    const float RoomMin = 4f, RoomMax = 188f;
    readonly BufferPool pool = new();
    Simulation simulation = null!;
    double accumulator;
    bool environmentReady, grabConstraintsActive;
    ConstraintHandle linearServo, angularServo;
    Vector3 localGrabPoint;
    float grabDistanceOffset;
    int nextBodyId = 1;
    Quaternion targetOrientation = Quaternion.Identity;

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
        grabConstraintsActive = false;
        grabDistanceOffset = 0f;
        accumulator = 0;
    }

    internal void Clear()
    {
        Held = null;
        Bodies.Clear();
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

    internal static Vector3 Extents(RigidCube body)
    {
        return RotatedExtents(body.HalfSize,body.Rotation);
    }

    static Vector3 RotatedExtents(float half,Quaternion rotation)
    {
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(rotation);
        float s = half;
        return new Vector3(
            s * (MathF.Abs(m.M11) + MathF.Abs(m.M21) + MathF.Abs(m.M31)),
            s * (MathF.Abs(m.M12) + MathF.Abs(m.M22) + MathF.Abs(m.M32)),
            s * (MathF.Abs(m.M13) + MathF.Abs(m.M23) + MathF.Abs(m.M33)));
    }

    static bool Overlap(Vector3 a, Vector3 ae, Vector3 b, Vector3 be) =>
        MathF.Abs(a.X - b.X) < ae.X + be.X && MathF.Abs(a.Y - b.Y) < ae.Y + be.Y && MathF.Abs(a.Z - b.Z) < ae.Z + be.Z;

    bool SpawnBlocked(Vector3 center, float half, IReadOnlyList<RectangleF> walls, Vector3 player)
    {
        Vector3 e = new(half);
        if (center.X - half < RoomMin || center.X + half > RoomMax || center.Z - half < RoomMin || center.Z + half > RoomMax ||
            center.Y - half < 0.01f || center.Y + half > Ceiling - 0.01f) return true;
        foreach (RectangleF wall in walls)
            if (center.X + half > wall.Left && center.X - half < wall.Right && center.Z + half > wall.Top && center.Z - half < wall.Bottom) return true;
        foreach (RigidCube other in Bodies)
            if (Overlap(center, e, other.Position, Extents(other))) return true;
        return MathF.Abs(center.X - player.X) < half + 0.3f && MathF.Abs(center.Z - player.Z) < half + 0.3f && center.Y - half < 1.9f;
    }

    internal bool Spawn(Vector3 eye, Vector3 forward, bool emissive, IReadOnlyList<RectangleF> walls, Vector3 player)
        => Spawn(eye, forward, emissive, walls, player, emissive ? new Vector3(0.2f, 0.72f, 1f) : new Vector3(0.82f, 0.69f, 0.22f));

    internal bool Spawn(Vector3 eye, Vector3 forward, bool emissive, IReadOnlyList<RectangleF> walls, Vector3 player, Vector3 color)
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
        Bodies.Add(new RigidCube { Id = nextBodyId++, Position = position, HalfSize = half, Emissive = emissive, Color = color, Handle = handle });
        return true;
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

    internal bool TryPickup(Vector3 eye, Vector3 direction, Quaternion cameraRotation, IReadOnlyList<RectangleF> walls)
    {
        if (Held != null) return false;
        float nearest = MathF.Min(PickupRange, WallRay(eye, direction, walls, PickupRange));
        RigidCube? candidate = null;
        foreach (RigidCube body in Bodies)
        {
            Quaternion inverse = Quaternion.Inverse(body.Rotation);
            float t = RayBox(Vector3.Transform(eye - body.Position, inverse), Vector3.Transform(direction, inverse), Vector3.Zero, new Vector3(body.HalfSize));
            if (t >= 0f && t < nearest) { nearest = t; candidate = body; }
        }
        if (candidate == null) return false;

        Held = candidate;
        candidate.RestDrag = candidate.Drag;
        candidate.Drag = 0.8f;
        Vector3 hit = eye + direction * nearest;
        Quaternion pickupOrientation = Quaternion.Normalize(simulation.Bodies[candidate.Handle].Pose.Orientation);
        localGrabPoint = Vector3.Transform(hit - candidate.Position, Quaternion.Inverse(pickupOrientation));
        grabDistanceOffset = nearest - HoldDistance;
        candidate.HoldTarget = hit;
        candidate.HoldTargetVelocity = Vector3.Zero;
        candidate.CarryRotationOffset = Quaternion.Normalize(Quaternion.Inverse(cameraRotation) * pickupOrientation);
        targetOrientation = pickupOrientation;
        AddOrUpdateGrabConstraints(hit, cameraRotation);
        candidate.HasHoldTarget = true;
        return true;
    }

    void AddOrUpdateGrabConstraints(Vector3 target, Quaternion cameraRotation)
    {
        if (Held == null) return;
        var linear = new OneBodyLinearServo
        {
            LocalOffset = localGrabPoint,
            Target = target,
            ServoSettings = new ServoSettings(float.MaxValue, 0f, 360f),
            SpringSettings = new SpringSettings(MathF.Max(0.1f, Frequency), MathF.Max(0.05f, Damping))
        };
        float side = Held.HalfSize * 2f;
        float inertia = side * side / 6f;
        var angular = new OneBodyAngularServo
        {
            TargetOrientation = targetOrientation,
            ServoSettings = new ServoSettings(12f, 0f, MathF.Max(10f, inertia * 260f)),
            SpringSettings = new SpringSettings(MathF.Max(0.1f, Frequency), MathF.Max(0.05f, Damping))
        };
        if (!grabConstraintsActive)
        {
            linearServo = simulation.Solver.Add(Held.Handle, linear);
            angularServo = simulation.Solver.Add(Held.Handle, angular);
            grabConstraintsActive = true;
        }
        else
        {
            simulation.Solver.ApplyDescription(linearServo, linear);
            simulation.Solver.ApplyDescription(angularServo, angular);
        }
        simulation.Bodies[Held.Handle].Activity.TimestepsUnderThresholdCount = 0;
    }

    internal void Release()
    {
        if (Held == null) return;
        if (grabConstraintsActive)
        {
            simulation.Solver.Remove(linearServo);
            simulation.Solver.Remove(angularServo);
            grabConstraintsActive = false;
        }
        RigidCube released = Held;
        released.HasHoldTarget = false;
        released.Drag = released.RestDrag;
        Held = null;
        grabDistanceOffset = 0f;
    }

    internal bool CollidesPlayer(float x, float z, float radius)
    {
        foreach (RigidCube body in Bodies)
        {
            if (body == Held) continue;
            Vector3 e = Extents(body);
            if (body.Position.Y - e.Y > 1.9f) continue;
            if (MathF.Abs(body.Position.X - x) < e.X + radius && MathF.Abs(body.Position.Z - z) < e.Z + radius) return true;
        }
        return false;
    }

    internal void Update(float dt, Vector3 eye, Vector3 forward, Quaternion cameraRotation, IReadOnlyList<RectangleF> walls, Vector3 player)
    {
        EnsureEnvironment(walls);
        accumulator = Math.Min(accumulator + Math.Clamp(dt, 0f, 0.05f), 0.1);
        Quaternion desiredOrientation = Held == null ? Quaternion.Identity : Quaternion.Normalize(cameraRotation * Held.CarryRotationOffset);
        Vector3 desiredTarget = eye + forward * MathF.Max(0f, HoldDistance + grabDistanceOffset);
        if (Held != null)
        {
            Vector3 worldGrabOffset = Vector3.Transform(localGrabPoint, desiredOrientation);
            float halfHeight = RotatedExtents(Held.HalfSize, desiredOrientation).Y;
            float centerY = Math.Clamp(desiredTarget.Y - worldGrabOffset.Y,
                halfHeight + 0.005f, Ceiling - halfHeight - 0.005f);
            desiredTarget.Y = centerY + worldGrabOffset.Y;
        }
        while (accumulator >= Step)
        {
            if (Held != null)
            {
                Vector3 previous = Held.HoldTarget;
                float blend = 1f - MathF.Exp(-MathF.Max(TargetSmoothing, 0.1f) * Step);
                Held.HoldTarget = Vector3.Lerp(Held.HoldTarget, desiredTarget, blend);
                Held.HoldTargetVelocity = (Held.HoldTarget - previous) / Step;
                targetOrientation = Quaternion.Normalize(Quaternion.Slerp(targetOrientation, desiredOrientation, blend));
                AddOrUpdateGrabConstraints(Held.HoldTarget, cameraRotation);
            }
            simulation.Timestep(Step, null);
            ApplyRestingResistance();
            accumulator -= Step;
        }
        SyncBodies();
    }

    void ApplyRestingResistance()
    {
        foreach (RigidCube body in Bodies)
        {
            if (body == Held) continue;
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
