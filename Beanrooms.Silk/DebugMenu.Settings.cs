using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    static (int Page,int Row) MappedSetting(int page,int row) => page switch
    {
        11 => (1,new[] { 0,7,9,10 }[row]),
        12 => (10,row),
        _ => (page,row)
    };
    void Adjust(int row, int direction,bool playSound=true)
    {
        if(Page>=11)
        {
            var (page,mappedRow)=MappedSetting(Page,row);
            int original=Page;
            Page=page;
            try { Adjust(mappedRow,direction,playSound); }
            finally { Page=original; }
            return;
        }
        if(Page == 5 && !MonsterControlsWritable) return;
        if (Page == 1)
        {
            switch (row)
            {
                case 0: RenderMode = RenderMode == 1 ? 2 : 1; break;
                case 1:
                    int sampleIndex = Array.IndexOf(SampleChoices, Samples);
                    Samples = SampleChoices[(sampleIndex + direction + SampleChoices.Length) % SampleChoices.Length];
                    break;
                case 2: Bounces = Math.Clamp(Bounces + direction, 1, 3); break;
                case 3: BounceStrength = Math.Clamp(BounceStrength + direction * 0.01f, 0f, 3f); break;
                case 4: AO = Math.Clamp(AO + direction * 0.01f, 0f, 1.5f); break;
                case 5: LightIntensity = Math.Clamp(LightIntensity + direction * 0.01f, 0.1f, 2f); break;
                case 6: LightDensity = Math.Clamp(LightDensity + direction * 0.01f, 0f, 1f); break;
                case 7: ResolutionIndex = (ResolutionIndex + direction + 3) % 3; break;
                case 8: Lights = !Lights; break;
                case 9: FpsLimit = FpsLimit == 0 ? (direction < 0 ? 240 : 15) : FpsLimit >= 240 && direction > 0 ? 0 : Math.Clamp(FpsLimit + direction,15,240); break;
                case 10: Vsync = !Vsync; break;
                case 11: Volumetrics = !Volumetrics; break;
                case 12: FogDensity = Math.Clamp(FogDensity + direction * 0.001f, 0f, 0.1f); break;
                case 13: FogSteps = Math.Clamp(FogSteps + direction, 2, 8); break;
                case 14: ProbeQuality = (ProbeQuality + direction + 4) % 4; break;
                case 15: ProbeBounces = Math.Clamp(ProbeBounces + direction, 1, 6); break;
                case 16: HybridDirectSamples = Math.Clamp(HybridDirectSamples + direction, 1, 8); break;
            }
        }
        else if(Page==10)
        {
            switch(row)
            {
                case 0: ShowFps=!ShowFps;break;
                case 1: ShowObjectFrames=!ShowObjectFrames;break;
                case 2: ShowPlayerTags=!ShowPlayerTags;break;
                case 3: ShowDebugOverlay=!ShowDebugOverlay;break;
                case 4: MonsterDebug=!MonsterDebug;break;
            }
        }
        else if(Page == 5)
        {
            switch(row)
            {
                case 0: MonsterSettings.Enabled=!MonsterSettings.Enabled; break;
                case 1: MonsterSettings.RoamSpeed=Math.Clamp(MonsterSettings.RoamSpeed+direction*0.1f,0.3f,4f); break;
                case 2: MonsterSettings.SearchSpeed=Math.Clamp(MonsterSettings.SearchSpeed+direction*0.1f,0.5f,8f); break;
                case 3: MonsterSettings.ChaseSpeed=Math.Clamp(MonsterSettings.ChaseSpeed+direction*0.1f,2f,14f); break;
                case 4: MonsterSettings.ChaseTurn=Math.Clamp(MonsterSettings.ChaseTurn+direction,20f,240f); break;
                case 5: MonsterSettings.SearchTurn=Math.Clamp(MonsterSettings.SearchTurn+direction,20f,240f); break;
                case 6: MonsterSettings.RoamTurn=Math.Clamp(MonsterSettings.RoamTurn+direction,20f,240f); break;
                case 7: MonsterSettings.SightRange=Math.Clamp(MonsterSettings.SightRange+direction*1f,5f,80f); break;
                case 8: MonsterSettings.SightAngle=Math.Clamp(MonsterSettings.SightAngle+direction,45f,360f); break;
                case 9: MonsterSettings.SearchRadius=Math.Clamp(MonsterSettings.SearchRadius+direction*0.1f,0.5f,5f); break;
                case 10: MonsterSettings.RoamRadius=Math.Clamp(MonsterSettings.RoamRadius+direction*1f,5f,60f); break;
            }
        }
        else if(Page == 6)
        {
            switch(row)
            {
                case 0: break;
                case 1: PlayerColorIndex=(PlayerColorIndex+direction+CubeColors.Length)%CubeColors.Length;
                    Vector3 color=CubeColors[PlayerColorIndex].Color;
                    PlayerRed=color.X;PlayerGreen=color.Y;PlayerBlue=color.Z;break;
                case 2: PlayerRed=Math.Clamp(PlayerRed+direction*0.01f,0.05f,1f);PlayerColorIndex=-1;break;
                case 3: PlayerGreen=Math.Clamp(PlayerGreen+direction*0.01f,0.05f,1f);PlayerColorIndex=-1;break;
                case 4: PlayerBlue=Math.Clamp(PlayerBlue+direction*0.01f,0.05f,1f);PlayerColorIndex=-1;break;
            }
        }
        else if(Page == 9)
        {
            if(row==0) SessionAction?.Invoke(0);
            else if(row==1) SetPage(6);
            else if(row==2) OpenCategories();
            else if(row==3) OpenCategories(developer:true);
            else if(row==4) OpenSpawn();
            else if(row==5) OpenHelp();
            else if(row==6) SessionAction?.Invoke(1);
            else if(row==7) SessionAction?.Invoke(2);
            else return;
        }
        else if(Page == 7)
        {
            switch(row)
            {
                case 0: PostEnabled=!PostEnabled;break;
                case 1: Exposure=Math.Clamp(Exposure+direction*0.01f,0.25f,3f);break;
                case 2: AutoExposure=!AutoExposure;break;
                case 3: Bloom=!Bloom;break;
                case 4: BloomStrength=Math.Clamp(BloomStrength+direction*0.01f,0f,2f);break;
                case 5: BloomThreshold=Math.Clamp(BloomThreshold+direction*0.01f,0.25f,4f);break;
                case 6: BloomRadius=Math.Clamp(BloomRadius+direction*0.01f,0.5f,4f);break;
                case 7: Dithering=!Dithering;break;
                case 8: PaletteReduction=!PaletteReduction;break;
            }
        }
        else
        {
            switch (row)
            {
                case 0: MouseSensitivity = Math.Clamp(MouseSensitivity + direction * 0.00001f,0.0004f,0.005f); break;
                case 1: IdleShake = Math.Clamp(IdleShake + direction * 0.01f,0f,4f); break;
                case 2: WalkShake = Math.Clamp(WalkShake + direction * 0.01f,0f,8f); break;
                case 3: SprintShake = Math.Clamp(SprintShake + direction * 0.01f,0f,8f); break;
                case 4: CrouchShake = Math.Clamp(CrouchShake + direction * 0.01f,0f,4f); break;
                case 5: HeadBob = Math.Clamp(HeadBob + direction * 0.01f,0f,2f); break;
                case 6: SwayAmount = Math.Clamp(SwayAmount + direction * 0.01f,0f,2f); break;
                case 7: StrafeRoll = Math.Clamp(StrafeRoll + direction * 0.1f,0f,5f); break;
                case 8: TurnRoll = Math.Clamp(TurnRoll + direction * 0.1f,0f,3f); break;
                case 9: MovePitch = Math.Clamp(MovePitch + direction * 0.1f,0f,4f); break;
                case 10: SwaySmooth = CameraSmooth = Math.Clamp(CameraSmooth + direction * 0.1f,1f,20f); break;
            }
        }
        Dirty = true;
        if(playSound) InteractionSound?.Invoke();
    }

    string Value(int row)
    {
        if(Page>=11)
        {
            var (page,mappedRow)=MappedSetting(Page,row);
            int original=Page;Page=page;
            try { return Value(mappedRow); }
            finally { Page=original; }
        }
        if(Page==10) return (row switch
        {
            0=>ShowFps,1=>ShowObjectFrames,2=>ShowPlayerTags,
            3=>ShowDebugOverlay,4=>MonsterDebug,_=>false
        })?"ON":"OFF";
        if(Page == 9) return row switch
        {
            0 => "PLAY",1=>"OPEN",2=>"OPEN",3=>"OPEN",4=>"OPEN",5=>"OPEN",
            6 => "MENU",7=>"EXIT",
            _ => ""
        };
        if(Page == 7) return row switch
        {
            0 => PostEnabled?"ON":"OFF",
            1 => $"{Exposure:0.00}",2 => AutoExposure?"ON":"OFF",
            3 => Bloom?"ON":"OFF",4 => $"{BloomStrength:0.00}",
            5 => $"{BloomThreshold:0.00}",6 => $"{BloomRadius:0.00}",
            7 => Dithering?"ON":"OFF",8 => PaletteReduction?"ON":"OFF",_ => ""
        };
        if(Page == 6) return row switch
        {
            0 => PlayerName,
            1 => PlayerColorIndex<0 ? "CUSTOM" : CubeColors[PlayerColorIndex].Name,
            2 => $"{PlayerRed:0.00}",3 => $"{PlayerGreen:0.00}",4 => $"{PlayerBlue:0.00}",
            _ => ""
        };
        if(Page == 5) return row switch
        {
            0 => MonsterSettings.Enabled ? "ON" : "OFF",
            1 => $"{MonsterSettings.RoamSpeed:0.0}",
            2 => $"{MonsterSettings.SearchSpeed:0.0}",
            3 => $"{MonsterSettings.ChaseSpeed:0.0}",
            4 => $"{MonsterSettings.ChaseTurn:0}",
            5 => $"{MonsterSettings.SearchTurn:0}",
            6 => $"{MonsterSettings.RoamTurn:0}",
            7 => $"{MonsterSettings.SightRange:0}",
            8 => $"{MonsterSettings.SightAngle:0}",
            9 => $"{MonsterSettings.SearchRadius:0.0}",
            10 => $"{MonsterSettings.RoamRadius:0}",
            _ => ""
        };
        if (Page == 3) return row switch
        {
            0 => $"{MouseSensitivity*1000f:0.00}",
            1 => $"{IdleShake:0.00}",
            2 => $"{WalkShake:0.00}",
            3 => $"{SprintShake:0.00}",
            4 => $"{CrouchShake:0.00}",
            5 => $"{HeadBob:0.00}",
            6 => $"{SwayAmount:0.00}",
            7 => $"{StrafeRoll:0.0}",
            8 => $"{TurnRoll:0.0}",
            9 => $"{MovePitch:0.0}",
            10 => $"{CameraSmooth:0.0}",
            _ => ""
        };
        return row switch
        {
            0 => RenderMode == 1 ? "REALTIME" : "PATH TRACED",
            1 => $"{Samples} SPP",
            2 => Bounces.ToString(),
            3 => $"{BounceStrength:0.00}",
            4 => $"{AO:0.00}",
            5 => $"{LightIntensity:0.00}",
            6 => $"{LightDensity:0.00}",
            7 => ResolutionIndex switch { 0 => "320x240", 1 => "480x360", _ => "640x480" },
            8 => Lights ? "ON" : "OFF",
            9 => FpsLimit == 0 ? "UNCAP" : $"{FpsLimit} FPS",
            10 => Vsync ? "ON" : "OFF",
            11 => Volumetrics ? "ON" : "OFF",
            12 => $"{FogDensity:0.000}",
            13 => FogSteps.ToString(),
            14 => ProbeQuality switch { 0 => "48", 1 => "96", 2 => "144", _ => "192" },
            15 => ProbeBounces.ToString(),
            16 => HybridDirectSamples.ToString(),
            _ => ""
        };
    }
}
