using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    internal void Toggle()
    {
        FinishEditing();
        ClearCommand();
        if(TitleScreen) { if(TitlePage==1) ShowTitle(resetHistory:false);return; }
        if(Open && SettingsFromTitle) { ShowTitle(resetHistory:false);return; }
        Open=!Open;TitleScreen=false;SettingsFromTitle=false;
        if(Open)
        {
            View=MenuView.Pause;Page=9;Selected=0;
            ResetTerminal();
            PrintLine("CLICK OR TYPE OPTION NAME, THEN ENTER");
            PrintLine("PAGE UP / PAGE DOWN / WHEEL SCROLL");
            PrintCurrentView();
        }
        Dirty=true;InteractionSound?.Invoke();
    }
    internal void Back()
    {
        FinishEditing();
        ClearCommand();
        if(TitleScreen) { if(TitlePage==1) ShowTitle(resetHistory:false);return; }
        if(View==MenuView.Settings)
        {
            View=settingsReturn;
            PrintCurrentView();
            Dirty=true;return;
        }
        if(View is MenuView.Categories or MenuView.Help or MenuView.Spawn)
        { if(SettingsFromTitle) ShowTitle(resetHistory:false); else { View=MenuView.Pause;Page=9;Selected=0;PrintCurrentView();Dirty=true; } return; }
        Toggle();
    }
    internal void OpenCategories(bool developer=false)
    {
        FinishEditing();
        ClearCommand();
        TitleScreen=false;developerCategories=developer;View=MenuView.Categories;CategorySelected=0;Dirty=true;InteractionSound?.Invoke();
        PrintCurrentView();
    }
    internal void OpenHelp()
    {
        FinishEditing();
        ClearCommand();
        TitleScreen=false;View=MenuView.Help;Dirty=true;InteractionSound?.Invoke();
        PrintCurrentView();
    }
    internal void OpenSpawn()
    {
        FinishEditing();
        ClearCommand();
        TitleScreen=false;View=MenuView.Spawn;Dirty=true;InteractionSound?.Invoke();
        PrintCurrentView();
    }
    internal void ShowTitle(bool resetHistory=true)
    {
        if(TitleScreen && TitlePage==1) CancelJoinAction?.Invoke();
        ClearCommand();
        if(resetHistory)
        {
            ResetTerminal();
            PrintLine("BEANROOMS COMMAND TERMINAL READY");
            PrintLine("CLICK OR TYPE OPTION NAME, THEN ENTER");
            PrintLine("PAGE UP / PAGE DOWN / WHEEL SCROLL");
        }
        FinishEditing(); Open=true;TitleScreen=true;SettingsFromTitle=false;
        TitlePage=0;Dirty=true;
        PrintCurrentView();
    }
    internal void LeaveTitle()
    {
        ClearCommand();
        FinishEditing(); Open=false;TitleScreen=false;SettingsFromTitle=false;ResetTerminal();Dirty=true;
    }
    internal void ShowJoinScreen()
    {
        ClearCommand();
        FinishEditing();TitleScreen=true;TitlePage=1;Dirty=true;
        PrintCurrentView();
    }
    internal void ShowSettingsFromTitle()
    {
        FinishEditing();TitleScreen=false;SettingsFromTitle=true;OpenCategories();
    }
    internal void SetPage(int page)
    {
        if(page is 0 or 2 or 4 or 8 || page<0 || page>=Labels.Length) return;
        ClearCommand();
        if(page!=9 && View!=MenuView.Settings)
            settingsReturn=View==MenuView.Pause?MenuView.Pause:MenuView.Categories;
        FinishEditing(); Page=Math.Clamp(page,0,12); Selected=0;
        View=Page==9?MenuView.Pause:MenuView.Settings;
        Dirty=true;InteractionSound?.Invoke();
        PrintCurrentView();
    }
    internal void ResetDefaults()
    {
        var defaults=new DebugMenu();
        switch(Page)
        {
            case 11:
                RenderMode=defaults.RenderMode;
                ResolutionIndex=defaults.ResolutionIndex;
                FpsLimit=defaults.FpsLimit;
                Vsync=defaults.Vsync;
                break;
            case 12:
                ShowFps=defaults.ShowFps;ShowObjectFrames=defaults.ShowObjectFrames;
                ShowPlayerTags=defaults.ShowPlayerTags;
                break;
            case 3:
                MouseSensitivity=defaults.MouseSensitivity;
                IdleShake=defaults.IdleShake;WalkShake=defaults.WalkShake;
                SprintShake=defaults.SprintShake;CrouchShake=defaults.CrouchShake;
                HeadBob=defaults.HeadBob;SwayAmount=defaults.SwayAmount;
                StrafeRoll=defaults.StrafeRoll;TurnRoll=defaults.TurnRoll;
                MovePitch=defaults.MovePitch;SwaySmooth=defaults.SwaySmooth;
                CameraSmooth=defaults.CameraSmooth;
                break;
            case 6:
                PlayerName=defaults.PlayerName;PlayerRed=defaults.PlayerRed;
                PlayerGreen=defaults.PlayerGreen;PlayerBlue=defaults.PlayerBlue;
                PlayerColorIndex=defaults.PlayerColorIndex;
                break;
            case 1:
                Samples=defaults.Samples;Bounces=defaults.Bounces;
                ProbeQuality=defaults.ProbeQuality;ProbeBounces=defaults.ProbeBounces;
                HybridDirectSamples=defaults.HybridDirectSamples;
                BounceStrength=defaults.BounceStrength;AO=defaults.AO;
                LightIntensity=defaults.LightIntensity;LightDensity=defaults.LightDensity;
                Lights=defaults.Lights;Volumetrics=defaults.Volumetrics;
                FogDensity=defaults.FogDensity;FogSteps=defaults.FogSteps;
                break;
            case 5:
                if(MonsterControlsWritable) MonsterSettings.CopyFrom(defaults.MonsterSettings);
                break;
            case 7:
                PostEnabled=defaults.PostEnabled;TonemapMode=defaults.TonemapMode;
                Exposure=defaults.Exposure;AutoExposure=defaults.AutoExposure;
                Bloom=defaults.Bloom;BloomStrength=defaults.BloomStrength;
                BloomThreshold=defaults.BloomThreshold;BloomRadius=defaults.BloomRadius;
                Dithering=defaults.Dithering;PaletteReduction=defaults.PaletteReduction;
                break;
            case 10:
                ShowDebugOverlay=defaults.ShowDebugOverlay;MonsterDebug=defaults.MonsterDebug;
                break;
        }
        FinishEditing();Dirty=true;
    }
    internal void FinishEditing() { JoinKey=JoinKey.Trim(); }
}
