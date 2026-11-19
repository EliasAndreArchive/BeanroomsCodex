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
    float crouchBlend, sprintBlend, gaitPhase, clock, turnRate, pitchTurnRate, gaitWeight, cameraSprint, cameraCrouch;
    Vector2 cameraVelocity, cameraAcceleration;
    Vector3 angularMotion, angularVelocity, positionalMotion, positionalVelocity, stanceMotion, stanceVelocity;
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
    static float HandNoise(float time,int seed)
    {
        // Different, warped octave clocks keep every axis wandering without the
        // pauses at lattice boundaries of a single slow value-noise channel.
        float warped=time+.32f*Noise(time*.37f+9.1f,seed+71);
        return .54f*Noise(warped,seed)+.29f*Noise(time*2.13f+17.7f,seed+13)
            +.13f*Noise(time*4.37f+39.3f,seed+29)+.04f*Noise(time*8.71f+61.1f,seed+47);
    }
    static float Limit(float value,float limit)=>limit*MathF.Tanh(value/limit);
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
        crouchBlend=sprintBlend=gaitPhase=clock=turnRate=pitchTurnRate=gaitWeight=cameraSprint=cameraCrouch=0f;
        cameraVelocity=cameraAcceleration=Vector2.Zero;
        stanceMotion=stanceVelocity=Vector3.Zero;
        angularMotion=angularVelocity=positionalMotion=positionalVelocity=Vector3.Zero;
        Roll=PitchOffset=YawOffset=0f;
        ViewOffset=Vector3.Zero;
    }

    internal void Update(float dt, Vector2 localInput, bool sprintHeld, bool crouchHeld,
        float mouseYawDelta, float yaw, DebugMenu settings, ref Vector3 camera,
        Func<float,float,float,bool> collides,bool allowTranslation,float mousePitchDelta=0f)
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
        // Camera envelopes lag the locomotion state, so a key press or collision
        // never swaps the noise amplitude or movement direction in one frame.
        gaitWeight=Follow(gaitWeight,movement,movement>gaitWeight?4.2f:5.5f,dt);
        cameraSprint=Follow(cameraSprint,sprintBlend,3.6f,dt);
        cameraCrouch=Follow(cameraCrouch,crouchBlend,4.0f,dt);
        Vector2 localVelocity=new(Vector2.Dot(velocity,right),Vector2.Dot(velocity,forward));
        Vector2 acceleration=dt>0.0001f?(velocity-previousVelocity)/dt:Vector2.Zero;
        Vector2 localAcceleration=new(Vector2.Dot(acceleration,right),Vector2.Dot(acceleration,forward));
        // Clamp the input to the camera filter, not its final pose; blocked motion
        // may stop instantly in physics while the camera settles into the stop.
        localAcceleration=Vector2.Clamp(localAcceleration,new(-18f),new(18f));
        cameraVelocity=Vector2.Lerp(cameraVelocity,localVelocity,1f-MathF.Exp(-6f*dt));
        cameraAcceleration=Vector2.Lerp(cameraAcceleration,localAcceleration,1f-MathF.Exp(-5f*dt));
        float maxSpeed=MovementProfile.WalkSpeed*MovementProfile.SprintMultiplier;

        float walkStrength=settings.WalkShake/5f;
        float runStrength=settings.SprintShake/7f;
        float crouchStrength=settings.CrouchShake/2f;
        float movingStrength=(walkStrength*(1f-cameraSprint)+1.85f*runStrength*cameraSprint)*(1f-cameraCrouch)
            +.55f*crouchStrength*cameraCrouch;
        float idleStrength=(settings.IdleShake*(1f-cameraCrouch)+settings.CrouchIdleShake*cameraCrouch)/3f;
        float handheld=idleStrength*(1f-gaitWeight)+movingStrength*gaitWeight;
        float noise=Math.Clamp(settings.HandheldNoise,0f,2f);
        float stride=(2.7f+.4f*cameraSprint)*(1f-cameraCrouch)+1.9f*cameraCrouch;
        gaitPhase+=actualSpeed*dt*Tau/stride*(1f+.13f*noise*HandNoise(clock*.81f,7));
        float phase=gaitPhase+.26f*noise*HandNoise(clock*1.11f,11);
        float bob=settings.HeadBob*gaitWeight*movingStrength;
        float asymmetry=1f+.36f*noise*HandNoise(clock*1.73f,13);
        // Spread the weight transfer over the whole step. There are no discrete
        // footfall velocity impulses, so pitch and translation remain continuous.
        float step=MathF.Sin(phase*2f)+.17f*MathF.Sin(phase*4f+.8f);
        float side=MathF.Sin(phase)+.22f*MathF.Sin(phase*2f+1.1f);
        float heel=MathF.Exp(-3.2f*(1f-MathF.Cos(phase*2f-.45f)))-.17f;
        Vector3 grip=new(HandNoise(clock*.79f,17),HandNoise(clock*.67f,23),HandNoise(clock*.89f,31));
        Vector3 travel=new(HandNoise(clock*1.29f,41),HandNoise(clock*1.13f,43),HandNoise(clock*1.47f,47));

        float measuredTurn=dt>0.0001f?Math.Clamp(mouseYawDelta/dt,-5f,5f):0f;
        turnRate=Follow(turnRate,measuredTurn,6f,dt);
        float measuredPitch=dt>0.0001f?Math.Clamp(mousePitchDelta/dt,-5f,5f):0f;
        pitchTurnRate=Follow(pitchTurnRate,measuredPitch,6f,dt);
        float sway=settings.SwayAmount;
        Vector3 targetAngles=new(
            Radians(noise*handheld*(.95f*grip.X+.13f*HandNoise(clock*4.1f,19)))
                +Radians(.92f)*bob*(step-.7f*heel)*asymmetry
                -sway*(cameraVelocity.Y/maxSpeed*Radians(settings.MovePitch)*.40f
                    +Limit(cameraAcceleration.Y*.0023f,.035f)+pitchTurnRate*.013f),
            Radians(noise*handheld*(1.30f*grip.Y+.18f*HandNoise(clock*3.7f,29)))
                +Radians(.90f)*bob*(MathF.Sin(phase+.6f)+.25f*travel.X)
                -sway*(turnRate*.011f+cameraVelocity.X/maxSpeed*.014f),
            Radians(noise*handheld*(1.20f*grip.Z+.15f*HandNoise(clock*4.3f,37)))
                +Radians(1.50f)*bob*side*asymmetry
                -sway*(cameraVelocity.X/maxSpeed*Radians(settings.StrafeRoll)*.60f
                    +turnRate*Radians(settings.TurnRoll)*.55f));
        Vector3 targetPosition=new(
            .033f*bob*side*asymmetry+.018f*noise*handheld*travel.X
                -sway*Limit(cameraAcceleration.X*.0014f,.03f),
            .039f*bob*(step-.55f*heel)*asymmetry+.012f*noise*handheld*travel.Y
                +.004f*noise*idleStrength*HandNoise(clock*.39f,59),
            .025f*bob*(MathF.Sin(phase*2f+.9f)+.25f*side)+.021f*noise*handheld*travel.Z
                -sway*Limit(cameraAcceleration.Y*.0019f,.035f));

        float springSpeed=Math.Clamp(settings.SwaySmooth/11f,.35f,2f);
        Spring(ref angularMotion,ref angularVelocity,targetAngles,2.15f*springSpeed,.86f,dt);
        Spring(ref positionalMotion,ref positionalVelocity,targetPosition,2.65f*springSpeed,.88f,dt);
        float stanceHeight=-(StandingEye-MovementProfile.CrouchHeight)*crouchBlend;
        Spring(ref stanceMotion,ref stanceVelocity,new Vector3(0f,stanceHeight,0f),2.0f,.90f,dt);
        // Soft limits retain velocity through large movements instead of clipping
        // the trajectory against a hard rotational/translation boundary.
        PitchOffset=Limit(angularMotion.X,.14f);
        YawOffset=Limit(angularMotion.Y,.08f);
        Roll=Limit(angularMotion.Z,.18f);
        Vector3 heldCamera=new(positionalMotion.X+YawOffset*.12f-Roll*.07f,
            positionalMotion.Y-PitchOffset*.08f,positionalMotion.Z+PitchOffset*.05f);
        Vector3 offset=new(Limit(heldCamera.X,.085f),Limit(heldCamera.Y,.11f),Limit(heldCamera.Z,.07f));
        camera.Y=StandingEye+stanceHeight;
        ViewOffset=new Vector3(right.X*offset.X+forward.X*offset.Z,
            offset.Y+stanceMotion.Y-stanceHeight,right.Y*offset.X+forward.Y*offset.Z);
    }
}
