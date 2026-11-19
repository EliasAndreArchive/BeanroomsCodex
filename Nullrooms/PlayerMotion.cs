using System.Numerics;

namespace Beanrooms;

internal static class MovementProfile
{
    internal const float WalkSpeed=3.2f, SprintMultiplier=1.75f, CrouchMultiplier=.48f;
    internal const float Acceleration=6f, Deceleration=9f, StanceSmooth=5f, SprintSmooth=5f;
    internal const float CrouchHeight=1.12f, StepLength=2.2f;
}

internal sealed class PlayerMotion
{
    const float StandingEye = 1.68f;
    const float Tau = MathF.PI * 2f;
    Vector2 velocity;
    float crouchBlend, sprintBlend, gaitPhase, clock, turnRate, gaitWeight;
    Vector3 angularMotion, angularVelocity, positionalMotion, positionalVelocity;
    internal float Roll { get; private set; }
    internal float PitchOffset { get; private set; }
    internal float YawOffset { get; private set; }
    internal Vector3 ViewOffset { get; private set; }
    internal float Speed => velocity.Length();
    internal float CrouchBlend => crouchBlend;
    internal float SprintBlend => sprintBlend;

    static float Follow(float current, float target, float rate, float dt) =>
        current + (target-current) * (1f-MathF.Exp(-MathF.Max(rate,0.01f)*dt));
    static float Radians(float degrees) => degrees * (MathF.PI/180f);

    // Smooth value noise gives a hand-operated camera wandering corrections rather
    // than a repeating motor vibration. The seed is fixed so tests/replays agree.
    static float Noise(float time,int seed)
    {
        static float Sample(int cell,int seed)
        {
            uint hash=unchecked((uint)cell*747796405u+(uint)seed*2891336453u);
            hash=(hash^(hash>>16))*2246822519u;
            hash=(hash^(hash>>13))*3266489917u;
            return (hash>>8)*(2f/16777215f)-1f;
        }
        int cell=(int)MathF.Floor(time);
        float t=time-cell;
        t=t*t*t*(t*(t*6f-15f)+10f);
        return Sample(cell,seed)+(Sample(cell+1,seed)-Sample(cell,seed))*t;
    }
    static void Spring(ref Vector3 position,ref Vector3 velocity,Vector3 target,float hz,float damping,float dt)
    {
        // Analytic underdamped spring: its rebound is stable at varying frame rates.
        float omega=Tau*hz,decay=MathF.Exp(-damping*omega*dt);
        float frequency=omega*MathF.Sqrt(1f-damping*damping);
        float sine=MathF.Sin(frequency*dt),cosine=MathF.Cos(frequency*dt);
        Vector3 offset=position-target;
        Vector3 next=target+decay*(offset*cosine+(velocity+damping*omega*offset)*(sine/frequency));
        velocity=decay*(velocity*cosine-(damping*omega*velocity+omega*omega*offset)*(sine/frequency));
        position=next;
    }
    internal void Reset()
    {
        velocity=Vector2.Zero;
        crouchBlend=sprintBlend=gaitPhase=clock=turnRate=gaitWeight=0f;
        angularMotion=angularVelocity=positionalMotion=positionalVelocity=Vector3.Zero;
        Roll=PitchOffset=YawOffset=0f;
        ViewOffset=Vector3.Zero;
    }

    internal void Update(float dt, Vector2 localInput, bool sprintHeld, bool crouchHeld,
        float mouseYawDelta, float yaw, DebugMenu settings, ref Vector3 camera,
        Func<float,float,float,bool> collides,bool allowTranslation)
    {
        dt=Math.Clamp(dt,0f,0.05f);
        clock+=dt;
        Vector2 previousVelocity=velocity;
        if(localInput.LengthSquared()>1f) localInput=Vector2.Normalize(localInput);
        if(!allowTranslation) localInput=Vector2.Zero;

        crouchBlend=Follow(crouchBlend,crouchHeld?1f:0f,MovementProfile.StanceSmooth,dt);
        sprintBlend=Follow(sprintBlend,sprintHeld&&!crouchHeld?1f:0f,MovementProfile.SprintSmooth,dt);
        float speedLimit=MovementProfile.WalkSpeed * (1f+(MovementProfile.SprintMultiplier-1f)*sprintBlend)
            * (1f+(MovementProfile.CrouchMultiplier-1f)*crouchBlend);
        Vector2 forward=new(MathF.Sin(yaw),MathF.Cos(yaw));
        Vector2 right=new(forward.Y,-forward.X);
        Vector2 desired=(right*localInput.X+forward*localInput.Y)*speedLimit;
        float response=desired.LengthSquared()>velocity.LengthSquared()+0.01f ||
            Vector2.Dot(desired,velocity)<0f ? MovementProfile.Acceleration : MovementProfile.Deceleration;
        velocity=Vector2.Lerp(velocity,desired,1f-MathF.Exp(-response*dt));
        if(velocity.LengthSquared()<0.0001f && desired.LengthSquared()==0f) velocity=Vector2.Zero;

        if(allowTranslation)
        {
            float nextX=camera.X+velocity.X*dt;
            if(!collides(nextX,camera.Z,0f)) camera.X=nextX;
            else velocity.X=0f;
            float nextZ=camera.Z+velocity.Y*dt;
            if(!collides(camera.X,nextZ,0f)) camera.Z=nextZ;
            else velocity.Y=0f;
        }
        else velocity=Vector2.Zero;

        float actualSpeed=velocity.Length();
        float movement=Math.Clamp(actualSpeed/(MovementProfile.WalkSpeed*.8f),0f,1f);
        gaitWeight=Follow(gaitWeight,movement,10f,dt);
        Vector2 localVelocity=new(Vector2.Dot(velocity,right),Vector2.Dot(velocity,forward));
        Vector2 acceleration=dt>0.0001f?(velocity-previousVelocity)/dt:Vector2.Zero;
        Vector2 localAcceleration=new(Vector2.Dot(acceleration,right),Vector2.Dot(acceleration,forward));
        float maxSpeed=MovementProfile.WalkSpeed*MovementProfile.SprintMultiplier;

        float walkStrength=settings.WalkShake/5f;
        float runStrength=settings.SprintShake/7f;
        float crouchStrength=settings.CrouchShake/2f;
        float movingStrength=(walkStrength*(1f-sprintBlend)+1.8f*runStrength*sprintBlend)*(1f-crouchBlend)
            +.55f*crouchStrength*crouchBlend;
        float idleStrength=(settings.IdleShake*(1f-crouchBlend)+settings.CrouchIdleShake*crouchBlend)/3f;
        float handheld=idleStrength*(1f-gaitWeight)+movingStrength*gaitWeight;
        float stride=(2.7f+.4f*sprintBlend)*(1f-crouchBlend)+1.9f*crouchBlend;
        float previousPhase=gaitPhase;
        // Distance-driven steps stop when blocked; small cadence variations break symmetry.
        gaitPhase+=actualSpeed*dt*Tau/stride*(1f+.06f*Noise(clock*.8f,7));
        float phase=gaitPhase;
        float bob=settings.HeadBob*gaitWeight*movingStrength;
        float step=MathF.Sin(phase*2f),side=MathF.Sin(phase);
        float asymmetry=1f+.16f*Noise(clock*1.9f,11);

        float measuredTurn=dt>0.0001f?Math.Clamp(mouseYawDelta/dt,-5f,5f):0f;
        turnRate=Follow(turnRate,measuredTurn,12f,dt);
        float sway=settings.SwayAmount;
        Vector3 targetAngles=new(
            Radians(handheld*(.30f*Noise(clock*.75f,17)+.10f*Noise(clock*4.7f,19)))
                +Radians(.65f)*bob*step
                -sway*(localVelocity.Y/maxSpeed*Radians(settings.MovePitch)*.35f
                    +Math.Clamp(localAcceleration.Y*.0015f,-.028f,.028f)),
            Radians(handheld*(.23f*Noise(clock*.61f,23)+.065f*Noise(clock*5.3f,29)))
                +Radians(.36f)*bob*MathF.Sin(phase+.6f)
                -sway*turnRate*.004f,
            Radians(handheld*(.42f*Noise(clock*.83f,31)+.11f*Noise(clock*3.9f,37)))
                +Radians(1.0f)*bob*side*asymmetry
                -sway*(localVelocity.X/maxSpeed*Radians(settings.StrafeRoll)*.45f
                    +turnRate*Radians(settings.TurnRoll)*.40f));
        Vector3 targetPosition=new(
            .018f*bob*side+.0035f*handheld*Noise(clock*1.1f,41)-sway*Math.Clamp(localAcceleration.X*.0007f,-.02f,.02f),
            .024f*bob*step*asymmetry+.003f*idleStrength*Noise(clock*.46f,43),
            .011f*bob*MathF.Sin(phase*2f+.9f)+.002f*handheld*Noise(clock*1.3f,47)
                -sway*Math.Clamp(localAcceleration.Y*.0009f,-.025f,.025f));

        if((int)(previousPhase/MathF.PI)!=(int)(phase/MathF.PI))
        {
            float impact=settings.HeadBob*movingStrength*gaitWeight;
            float foot=((int)(phase/MathF.PI)&1)==0?1f:-1f;
            float variation=1f+.18f*Noise(clock*2.7f,53);
            angularVelocity+=new Vector3(-Radians(21f),foot*Radians(4f),foot*Radians(12f))*impact*variation;
            positionalVelocity+=new Vector3(foot*.075f,-.40f,.10f)*impact*variation;
        }
        float springSpeed=Math.Clamp(settings.SwaySmooth/11f,.35f,2f);
        Spring(ref angularMotion,ref angularVelocity,targetAngles,3.7f*springSpeed,.67f,dt);
        Spring(ref positionalMotion,ref positionalVelocity,targetPosition,4.6f*springSpeed,.72f,dt);
        // Preserve the responsive look direction while adding a bounded handheld pose.
        PitchOffset=Math.Clamp(angularMotion.X,-.14f,.14f);
        YawOffset=Math.Clamp(angularMotion.Y,-.08f,.08f);
        Roll=Math.Clamp(angularMotion.Z,-.18f,.18f);
        Vector3 offset=Vector3.Clamp(positionalMotion,new Vector3(-.085f,-.11f,-.07f),new Vector3(.085f,.11f,.07f));
        camera.Y=StandingEye-(StandingEye-MovementProfile.CrouchHeight)*crouchBlend;
        ViewOffset=new Vector3(right.X*offset.X+forward.X*offset.Z,offset.Y,
            right.Y*offset.X+forward.Y*offset.Z);
    }
}
