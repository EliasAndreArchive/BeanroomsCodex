using System.Numerics;
using Beanrooms;

internal static class CameraMotionChecks
{
    readonly record struct Sample(float AngularRms,float PositionRms,Vector3 Angles,Vector3 Offset,float Speed);
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    static Sample Simulate(bool moving,bool sprint,bool crouch,float dt=1f/120f,bool blocked=false,DebugMenu? settings=null)
    {
        var motion=new PlayerMotion();settings??=new DebugMenu();Vector3 camera=new(96,1.68f,96);
        var angles=new List<Vector3>();var offsets=new List<Vector3>();
        for(int i=0;i<(int)MathF.Round(14f/dt);i++) {
            motion.Update(dt,moving?Vector2.UnitY:Vector2.Zero,sprint,crouch,0,0,settings,ref camera,(_,_,_)=>blocked,true);
            Check(float.IsFinite(motion.Roll)&&float.IsFinite(motion.PitchOffset)&&float.IsFinite(motion.YawOffset)&&
                float.IsFinite(motion.ViewOffset.Length()),"Camera pose became non-finite.");
            Check(Math.Abs(motion.Roll)<=.18f&&Math.Abs(motion.PitchOffset)<=.14f&&Math.Abs(motion.YawOffset)<=.08f,
                "Camera wobble exceeded its rotation bounds.");
            if(i*dt>3) {angles.Add(new(motion.PitchOffset,motion.YawOffset,motion.Roll));offsets.Add(motion.ViewOffset);}
        }
        float Rms(List<Vector3> values) {
            Vector3 mean=values.Aggregate(Vector3.Zero,(a,b)=>a+b)/values.Count;
            return MathF.Sqrt(values.Sum(v=>Vector3.DistanceSquared(v,mean))/values.Count);
        }
        return new(Rms(angles),Rms(offsets),new(motion.PitchOffset,motion.YawOffset,motion.Roll),motion.ViewOffset,motion.Speed);
    }
    internal static void Run()
    {
        var idle=Simulate(false,false,false);var crouchIdle=Simulate(false,false,true);
        var walk=Simulate(true,false,false);var run=Simulate(true,true,false);var crouch=Simulate(true,false,true);
        Check(run.AngularRms>walk.AngularRms*1.25f&&walk.AngularRms>crouch.AngularRms*1.25f,
            "Running, walking and crouch walking need distinct camera strengths.");
        Check(run.PositionRms>walk.PositionRms*1.25f&&walk.PositionRms>crouch.PositionRms*1.25f,
            "Running/walking/crouch walking bob strengths are not ordered.");
        Check(idle.AngularRms>crouchIdle.AngularRms*1.5f&&crouch.AngularRms>crouchIdle.AngularRms,
            "Crouching at rest should be steadier than standing or crouch walking.");
        Check(Math.Abs(walk.Speed-3.2f)<.001f&&Math.Abs(run.Speed-5.6f)<.001f&&Math.Abs(crouch.Speed-1.536f)<.001f,
            "Camera work changed movement speeds.");
        var blocked=Simulate(true,true,false,blocked:true);
        Check(blocked.Speed==0&&Math.Abs(blocked.AngularRms-idle.AngularRms)<.00001f,
            "Walking into a wall must stop footstep bob.");
        foreach(float dt in new[] {1f/30f,1f/60f,1f/144f}) {
            var sample=Simulate(true,true,false,dt);
            Check(Math.Abs(sample.AngularRms/run.AngularRms-1)<.15f&&Math.Abs(sample.PositionRms/run.PositionRms-1)<.15f,
                "Camera motion strength depends excessively on frame rate.");
        }
        var disabled=new DebugMenu {IdleShake=0,CrouchIdleShake=0,WalkShake=0,SprintShake=0,CrouchShake=0,HeadBob=0,SwayAmount=0};
        var steady=Simulate(true,true,false,settings:disabled);
        Check(steady.AngularRms==0&&steady.PositionRms==0,"Disabling camera motion did not produce a steady view.");
        var motion=new PlayerMotion();var settings=new DebugMenu();var position=new Vector3(96,1.68f,96);
        Vector3 first=default;
        for(int pass=0;pass<2;pass++) {
            motion.Reset();position=new(96,1.68f,96);
            for(int frame=0;frame<240;frame++) motion.Update(1f/60,Vector2.UnitY,true,false,.005f,0,settings,ref position,(_,_,_)=>false,true);
            var end=new Vector3(motion.Roll,motion.PitchOffset,motion.YawOffset);
            if(pass==0)first=end;else Check(end==first,"Camera reset left old drift or spring state behind.");
        }
        // Rapid start/stop/stance/turn changes must remain continuous and bounded.
        motion.Reset();position=new(96,1.68f,96);Vector3 previousAngles=Vector3.Zero,previousOffset=Vector3.Zero;
        for(int frame=0;frame<900;frame++) {
            bool moving=frame%180<120,crouching=frame%240>=120;
            motion.Update(1f/60,moving?Vector2.UnitY:Vector2.Zero,true,crouching,frame%120<60?.03f:-.03f,
                0,settings,ref position,(_,_,_)=>false,true);
            var angles=new Vector3(motion.PitchOffset,motion.YawOffset,motion.Roll);
            Check(Vector3.Distance(angles,previousAngles)<.08f&&Vector3.Distance(motion.ViewOffset,previousOffset)<.05f,
                "Camera stance or gait transition produced a discontinuity.");
            previousAngles=angles;previousOffset=motion.ViewOffset;
        }
        var menu=new DebugMenu();menu.ShowTitle();menu.ShowSettingsFromTitle();menu.PasteCommand("CAMERA");menu.SubmitCommand();
        Check(menu.Page==3,"Camera settings are not accessible from CMD settings.");
        menu.PasteCommand("CROUCH IDLE SHAKE 0");menu.SubmitCommand();Check(menu.CrouchIdleShake==0,"Crouch idle setting did not apply.");
        menu.PasteCommand("RESET");menu.SubmitCommand();Check(menu.CrouchIdleShake==1.3f,"Camera reset missed crouch idle strength.");
        Console.WriteLine($"PASS: camcorder motion; rotation RMS idle {idle.AngularRms*180/MathF.PI:0.00}, crouched {crouchIdle.AngularRms*180/MathF.PI:0.00}, walk {walk.AngularRms*180/MathF.PI:0.00}, sprint {run.AngularRms*180/MathF.PI:0.00}, crouch walk {crouch.AngularRms*180/MathF.PI:0.00} degrees; 30/60/144 Hz, blocked motion, reset, controls.");
    }
}
