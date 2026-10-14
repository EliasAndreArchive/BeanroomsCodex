using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;

namespace Beanrooms;

// The title scene has its own small physics world. None of these bodies is a player.
internal sealed class MenuBeanSimulation : IDisposable
{
    internal const int Count = 7;
    readonly BufferPool pool = new();
    readonly Simulation simulation;
    readonly BodyHandle[] beans = new BodyHandle[Count];
    readonly float[] positions = new float[Count * 3];
    readonly float[] rotations = new float[Count * 4];
    readonly float[] colors =
    [
        1f,1f,1f, 1f,1f,1f, 1f,1f,1f, 1f,1f,1f,
        1f,1f,1f, 1f,1f,1f, 1f,1f,1f
    ];
    double accumulator;
    float elapsed;
    readonly Random random = new(7193);
    int impulseTick;

    internal MenuBeanSimulation()
    {
        simulation = Simulation.Create(pool,
            new PhysicsNarrowPhaseCallbacks(new SpringSettings(24f,1.1f),2f,0.25f),
            new PhysicsPoseCallbacks(Vector3.Zero,0.004f,0.005f),
            new SolveDescription(8,2,128),null,null);
        var capsule = new Capsule(0.46f,0.8f);
        TypedIndex capsuleIndex = simulation.Shapes.Add(in capsule);
        BodyInertia inertia = capsule.ComputeInertia(1f);
        Vector3[] starts =
        [
            new(-2.25f,1.45f,1.1f), new(1.4f,-1.2f,1.8f), new(3.0f,0.75f,2.4f),
            new(-0.2f,1.9f,3.2f), new(-3.15f,-1.0f,3.7f),
            new(1.8f,1.7f,4.4f), new(0.1f,-1.8f,4.9f)
        ];
        for(int i=0;i<Count;i++)
        {
            Vector3 linear = new(MathF.Sin(i*2.3f)*2.4f,MathF.Cos(i*2.0f)*2.0f,
                MathF.Sin(i*1.7f+0.4f)*1.3f);
            Vector3 angular = new(1.4f+i*0.25f,1.8f-i*0.2f,0.9f+i*0.25f);
            var description=BodyDescription.CreateDynamic(
                new RigidPose(starts[i],Quaternion.CreateFromYawPitchRoll(i*0.65f,i*0.21f,i*0.3f)),
                new BodyVelocity(linear,angular),inertia,
                new CollidableDescription(capsuleIndex,0.1f),new BodyActivityDescription(-1f));
            beans[i]=simulation.Bodies.Add(description);
        }
        AddWall(new Vector3(-4.5f,0f,2.5f),new Vector3(0.3f,8f,9f));
        AddWall(new Vector3(4.5f,0f,2.5f),new Vector3(0.3f,8f,9f));
        AddWall(new Vector3(0f,-3.0f,2.5f),new Vector3(9f,0.3f,9f));
        AddWall(new Vector3(0f,3.0f,2.5f),new Vector3(9f,0.3f,9f));
        AddWall(new Vector3(0f,0f,-0.5f),new Vector3(9f,8f,0.3f));
        AddWall(new Vector3(0f,0f,5.5f),new Vector3(9f,8f,0.3f));
        CopyState();
    }

    void AddWall(Vector3 center,Vector3 size)
    {
        var wall=new Box(size.X,size.Y,size.Z);
        TypedIndex index=simulation.Shapes.Add(in wall);
        simulation.Statics.Add(new StaticDescription(new RigidPose(center),index));
    }

    internal void Step(float dt)
    {
        accumulator+=Math.Clamp(dt,0f,0.05f);
        while(accumulator>=1.0/60.0)
        {
            elapsed+=1f/60f;
            bool kick=(int)(elapsed*1.5f)>impulseTick;
            if(kick) impulseTick=(int)(elapsed*1.5f);
            for(int i=0;i<Count;i++)
            {
                var body=simulation.Bodies[beans[i]];
                Vector3 p=body.Pose.Position;
                // Periodic crosscurrents keep collisions and tumbling unpredictable.
                Vector3 target=new(-p.Y*0.42f,p.X*0.34f,MathF.Sin(elapsed*1.8f+i)*0.55f);
                body.Velocity.Linear+=target*(1f/60f);
                if(kick)
                {
                    body.Velocity.Linear+=new Vector3(NextKick(),NextKick(),NextKick()*0.7f);
                    body.Velocity.Angular+=new Vector3(NextKick(),NextKick(),NextKick());
                }
                float speed=body.Velocity.Linear.Length();
                if(speed>4.5f) body.Velocity.Linear*=4.5f/speed;
            }
            simulation.Timestep(1f/60f);
            accumulator-=1.0/60.0;
        }
        CopyState();
    }

    float NextKick() => (random.NextSingle()-0.5f)*2.4f;

    void CopyState()
    {
        for(int i=0;i<Count;i++)
        {
            var body=simulation.Bodies[beans[i]];
            Vector3 p=body.Pose.Position;
            Quaternion q=Quaternion.Normalize(body.Pose.Orientation);
            positions[i*3]=p.X;positions[i*3+1]=p.Y;positions[i*3+2]=p.Z;
            rotations[i*4]=q.X;rotations[i*4+1]=q.Y;rotations[i*4+2]=q.Z;rotations[i*4+3]=q.W;
        }
    }

    internal void Upload(int program)
    {
        Gl.Uniform1i(Gl.GetUniformLocation(program,"beanCount"),Count);
        Gl.Uniform3fv(Gl.GetUniformLocation(program,"beanPosition[0]"),Count,positions);
        Gl.Uniform4fv(Gl.GetUniformLocation(program,"beanRotation[0]"),Count,rotations);
        Gl.Uniform3fv(Gl.GetUniformLocation(program,"beanColor[0]"),Count,colors);
    }

    public void Dispose()
    {
        simulation.Dispose();
        pool.Clear();
    }
}
