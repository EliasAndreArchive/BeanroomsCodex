using System.Numerics;

namespace Beanrooms;

internal sealed class PlayerMotion
{
    const float StandingEye = 1.68f;
    internal const float MaxStepHeight = 0.42f;
    const float Tau = MathF.PI * 2f;
    Vector2 velocity;
    float crouchBlend, sprintBlend, gaitPhase, clock, turnRate, lastForwardSpeed, visualFootElevation;
    float smoothRoll, smoothPitch, smoothYaw;
    internal float Roll { get; private set; }
    internal float PitchOffset { get; private set; }
    internal float YawOffset { get; private set; }
    internal Vector3 ViewOffset { get; private set; }
    internal float Speed => velocity.Length();
    internal float CrouchBlend => crouchBlend;
    internal float SprintBlend => sprintBlend;
    internal float FootElevation { get; private set; }

    static float Follow(float current, float target, float rate, float dt) =>
        current + (target-current) * (1f-MathF.Exp(-MathF.Max(rate,0.01f)*dt));
    static float Radians(float degrees) => degrees * (MathF.PI/180f);

    internal void Reset()
    {
        velocity=Vector2.Zero;
        crouchBlend=sprintBlend=gaitPhase=turnRate=lastForwardSpeed=0f;
        smoothRoll=smoothPitch=smoothYaw=Roll=PitchOffset=YawOffset=0f;
        FootElevation=visualFootElevation=0f;
        ViewOffset=Vector3.Zero;
    }

    internal void Update(float dt, Vector2 localInput, bool sprintHeld, bool crouchHeld,
        float mouseYawDelta, float yaw, DebugMenu settings, ref Vector3 camera,
        Func<float,float,float,bool> collides, Func<float,float,float> supportHeight,bool allowTranslation)
    {
        dt=Math.Clamp(dt,0f,0.05f);
        clock+=dt;
        if(localInput.LengthSquared()>1f) localInput=Vector2.Normalize(localInput);
        if(!allowTranslation) localInput=Vector2.Zero;

        crouchBlend=Follow(crouchBlend,crouchHeld?1f:0f,settings.StanceSmooth,dt);
        sprintBlend=Follow(sprintBlend,sprintHeld&&!crouchHeld?1f:0f,settings.SprintSmooth,dt);
        float speedLimit=settings.WalkSpeed * (1f+(settings.SprintMultiplier-1f)*sprintBlend)
            * (1f+(settings.CrouchMultiplier-1f)*crouchBlend);
        Vector2 forward=new(MathF.Sin(yaw),MathF.Cos(yaw));
        Vector2 right=new(forward.Y,-forward.X);
        Vector2 desired=(right*localInput.X+forward*localInput.Y)*speedLimit;
        float response=desired.LengthSquared()>velocity.LengthSquared()+0.01f ||
            Vector2.Dot(desired,velocity)<0f ? settings.Acceleration : settings.Deceleration;
        velocity=Vector2.Lerp(velocity,desired,1f-MathF.Exp(-response*dt));
        if(velocity.LengthSquared()<0.0001f && desired.LengthSquared()==0f) velocity=Vector2.Zero;

        if(allowTranslation)
        {
            float nextX=camera.X+velocity.X*dt;
            float xSupport=supportHeight(nextX,camera.Z);
            float xFoot=MathF.Max(FootElevation,xSupport);
            if(xSupport<=FootElevation+MaxStepHeight && !collides(nextX,camera.Z,xFoot))
            { camera.X=nextX;FootElevation=xFoot; }
            else velocity.X=0f;
            float nextZ=camera.Z+velocity.Y*dt;
            float zSupport=supportHeight(camera.X,nextZ);
            float zFoot=MathF.Max(FootElevation,zSupport);
            if(zSupport<=FootElevation+MaxStepHeight && !collides(camera.X,nextZ,zFoot))
            { camera.Z=nextZ;FootElevation=zFoot; }
            else velocity.Y=0f;
            // Physical feet follow the support immediately for collision; the
            // view follows smoothly so stepping onto low support has no camera pop.
            float settledSupport=supportHeight(camera.X,camera.Z);
            if(settledSupport<=FootElevation+MaxStepHeight) FootElevation=settledSupport;
        }
        else velocity=Vector2.Zero;

        visualFootElevation=Follow(visualFootElevation,FootElevation,11f,dt);

        float actualSpeed=velocity.Length();
        float movement=Math.Clamp(actualSpeed/MathF.Max(settings.WalkSpeed*0.75f,0.2f),0f,1f);
        Vector2 localVelocity=new(Vector2.Dot(velocity,right),Vector2.Dot(velocity,forward));
        float maxSpeed=MathF.Max(settings.WalkSpeed*settings.SprintMultiplier,0.2f);
        gaitPhase+=actualSpeed*dt*Tau/MathF.Max(settings.StepLength,0.5f);
        float gait=MathF.Sin(gaitPhase);
        float bobAmplitude=settings.HeadBob*movement*(0.011f+0.013f*sprintBlend)*(1f-0.45f*crouchBlend);

        float movingShake=settings.WalkShake*(1f-sprintBlend)+settings.SprintShake*sprintBlend;
        movingShake=movingShake*(1f-crouchBlend)+settings.CrouchShake*crouchBlend;
        float shake=settings.IdleShake*(1f-movement)+movingShake*movement;
        float slowNoise=MathF.Sin(clock*1.31f)+0.43f*MathF.Sin(clock*2.53f+1.2f);
        float pitchNoise=0.0013f*shake*(slowNoise+0.23f*MathF.Sin(clock*11.7f+2.1f));
        float rollNoise=0.0015f*shake*(MathF.Sin(clock*1.73f+0.7f)+0.3f*MathF.Sin(clock*8.9f));
        float yawNoise=0.0007f*shake*MathF.Sin(clock*1.11f+2.7f);

        float measuredTurn=dt>0.0001f?Math.Clamp(mouseYawDelta/dt,-5f,5f):0f;
        turnRate=Follow(turnRate,measuredTurn,12f,dt);
        float forwardAcceleration=dt>0.0001f?(localVelocity.Y-lastForwardSpeed)/dt:0f;
        lastForwardSpeed=localVelocity.Y;
        float targetRoll=settings.SwayAmount*(
            -localVelocity.X/maxSpeed*Radians(settings.StrafeRoll)
            -turnRate*Radians(settings.TurnRoll))
            +gait*bobAmplitude*0.28f;
        float targetPitch=settings.SwayAmount*(
            -localVelocity.Y/maxSpeed*Radians(settings.MovePitch)
            -Math.Clamp(forwardAcceleration*0.0023f,-0.022f,0.022f));
        float targetYaw=gait*bobAmplitude*0.09f;
        smoothRoll=Follow(smoothRoll,targetRoll,settings.SwaySmooth,dt);
        smoothPitch=Follow(smoothPitch,targetPitch,settings.SwaySmooth,dt);
        smoothYaw=Follow(smoothYaw,targetYaw,settings.SwaySmooth,dt);
        Roll=smoothRoll+rollNoise;
        PitchOffset=smoothPitch+pitchNoise;
        YawOffset=smoothYaw+yawNoise;

        camera.Y=StandingEye+visualFootElevation-(StandingEye-settings.CrouchHeight)*crouchBlend
            +bobAmplitude*MathF.Sin(gaitPhase*2f)
            +0.002f*settings.IdleShake*(1f-movement)*slowNoise;
        float sideMotion=settings.SwayAmount*(
            0.48f*bobAmplitude*gait
            +0.0025f*shake*MathF.Sin(clock*1.47f+0.9f));
        float forwardMotion=settings.SwayAmount*(
            0.27f*bobAmplitude*MathF.Cos(gaitPhase*2f)
            +0.0018f*shake*MathF.Sin(clock*1.93f+1.6f));
        ViewOffset=new Vector3(right.X*sideMotion+forward.X*forwardMotion,0f,
            right.Y*sideMotion+forward.Y*forwardMotion);
    }
}
