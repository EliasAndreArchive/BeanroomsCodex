using System.Globalization;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    readonly record struct NumericControl(float Min,float Max,float Step,float Current);

    (string Text,int Start,int End) SettingLine(int row)
    {
        string label=Labels[Page][row];
        if(TryNumericControl(row,out NumericControl numeric))
        {
            string min=numeric.Min.ToString("0.###",CultureInfo.InvariantCulture);
            string max=numeric.Max.ToString("0.###",CultureInfo.InvariantCulture);
            int position=(int)MathF.Round(Math.Clamp((numeric.Current-numeric.Min)/(numeric.Max-numeric.Min),0,1)*6);
            char[] track="-------".ToCharArray();
            track[position]='X';
            string prefix=$"{label} {min} [";
            string text=$"{prefix}{new string(track)}] {max} {Value(row)}";
            return (text,prefix.Length,prefix.Length+6);
        }
        return ($"{label} <{Value(row)}>",-1,-1);
    }

    bool TryNumericControl(int row,out NumericControl control)
    {
        int page=Page;
        if(page>=11) (page,row)=MappedSetting(page,row);
        control=(page,row) switch
        {
            (1,2)=>new(1,3,1,Bounces),
            (1,3)=>new(0,3,.01f,BounceStrength),
            (1,4)=>new(0,1.5f,.01f,AO),
            (1,5)=>new(.1f,2,.01f,LightIntensity),
            (1,6)=>new(0,1,.01f,LightDensity),
            (1,9)=>new(0,240,1,FpsLimit),
            (1,12)=>new(0,.1f,.001f,FogDensity),
            (1,13)=>new(2,8,1,FogSteps),
            (1,15)=>new(1,6,1,ProbeBounces),
            (1,16)=>new(1,8,1,HybridDirectSamples),
            (7,1)=>new(.25f,3,.01f,Exposure),
            (7,4)=>new(0,2,.01f,BloomStrength),
            (7,5)=>new(.25f,4,.01f,BloomThreshold),
            (7,6)=>new(.5f,4,.01f,BloomRadius),
            (5,1)=>new(.3f,4,.1f,MonsterSettings.RoamSpeed),
            (5,2)=>new(.5f,8,.1f,MonsterSettings.SearchSpeed),
            (5,3)=>new(2,14,.1f,MonsterSettings.ChaseSpeed),
            (5,4)=>new(20,240,1,MonsterSettings.ChaseTurn),
            (5,5)=>new(20,240,1,MonsterSettings.SearchTurn),
            (5,6)=>new(20,240,1,MonsterSettings.RoamTurn),
            (5,7)=>new(5,80,1,MonsterSettings.SightRange),
            (5,8)=>new(45,360,1,MonsterSettings.SightAngle),
            (5,9)=>new(.5f,5,.1f,MonsterSettings.SearchRadius),
            (5,10)=>new(5,60,1,MonsterSettings.RoamRadius),
            (3,0)=>new(.4f,5,.01f,MouseSensitivity*1000),
            (3,1)=>new(0,4,.01f,IdleShake),
            (3,2)=>new(0,8,.01f,WalkShake),
            (3,3)=>new(0,8,.01f,SprintShake),
            (3,4)=>new(0,4,.01f,CrouchShake),
            (3,5)=>new(0,2,.01f,HeadBob),
            (3,6)=>new(0,2,.01f,SwayAmount),
            (3,7)=>new(0,5,.1f,StrafeRoll),
            (3,8)=>new(0,3,.1f,TurnRoll),
            (3,9)=>new(0,4,.1f,MovePitch),
            (3,10)=>new(1,20,.1f,CameraSmooth),
            _=>default
        };
        return control.Max>control.Min;
    }

    void SetNumericFraction(int row,float fraction)
    {
        if(Page==5 && !MonsterControlsWritable) return;
        if(!TryNumericControl(row,out NumericControl control)) return;
        int page=Page;
        if(page>=11) (page,row)=MappedSetting(page,row);
        float target=Math.Clamp(control.Min+MathF.Round((control.Max-control.Min)*fraction/control.Step)*control.Step,control.Min,control.Max);
        switch(page,row)
        {
            case (1,2): Bounces=(int)target;break;
            case (1,3): BounceStrength=target;break;
            case (1,4): AO=target;break;
            case (1,5): LightIntensity=target;break;
            case (1,6): LightDensity=target;break;
            case (1,9): FpsLimit=target<15?0:(int)target;break;
            case (1,12): FogDensity=target;break;
            case (1,13): FogSteps=(int)target;break;
            case (1,15): ProbeBounces=(int)target;break;
            case (1,16): HybridDirectSamples=(int)target;break;
            case (7,1): Exposure=target;break;
            case (7,4): BloomStrength=target;break;
            case (7,5): BloomThreshold=target;break;
            case (7,6): BloomRadius=target;break;
            case (5,1): MonsterSettings.RoamSpeed=target;break;
            case (5,2): MonsterSettings.SearchSpeed=target;break;
            case (5,3): MonsterSettings.ChaseSpeed=target;break;
            case (5,4): MonsterSettings.ChaseTurn=target;break;
            case (5,5): MonsterSettings.SearchTurn=target;break;
            case (5,6): MonsterSettings.RoamTurn=target;break;
            case (5,7): MonsterSettings.SightRange=target;break;
            case (5,8): MonsterSettings.SightAngle=target;break;
            case (5,9): MonsterSettings.SearchRadius=target;break;
            case (5,10): MonsterSettings.RoamRadius=target;break;
            case (3,0): MouseSensitivity=target/1000;break;
            case (3,1): IdleShake=target;break;
            case (3,2): WalkShake=target;break;
            case (3,3): SprintShake=target;break;
            case (3,4): CrouchShake=target;break;
            case (3,5): HeadBob=target;break;
            case (3,6): SwayAmount=target;break;
            case (3,7): StrafeRoll=target;break;
            case (3,8): TurnRoll=target;break;
            case (3,9): MovePitch=target;break;
            case (3,10): SwaySmooth=CameraSmooth=target;break;
        }
        Dirty=true;
    }
}
