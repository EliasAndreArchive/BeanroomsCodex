using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    internal void Toggle()
    {
        FinishEditing();
        if(TitleScreen) { if(TitlePage==1) ShowTitle();return; }
        if(Open && SettingsFromTitle) { ShowTitle();return; }
        Open=!Open;TitleScreen=false;SettingsFromTitle=false;
        if(Open) { View=MenuView.Pause;Page=9;Selected=0; }
        hoveredItem=-1;Dirty=true;InteractionSound?.Invoke();
    }
    internal void Back()
    {
        FinishEditing();
        if(TitleScreen) { if(TitlePage==1) ShowTitle();return; }
        if(View==MenuView.Settings)
        {
            View=settingsReturn;
            Dirty=true;return;
        }
        if(View is MenuView.Categories or MenuView.Help)
        { if(SettingsFromTitle) ShowTitle(); else { View=MenuView.Pause;Page=9;Selected=0;Dirty=true; } return; }
        Toggle();
    }
    internal void OpenCategories(bool developer=false)
    {
        FinishEditing();
        TitleScreen=false;developerCategories=developer;View=MenuView.Categories;CategorySelected=0;Dirty=true;InteractionSound?.Invoke();
    }
    internal void OpenHelp()
    {
        FinishEditing();
        TitleScreen=false;View=MenuView.Help;Dirty=true;InteractionSound?.Invoke();
    }
    internal void ShowTitle()
    {
        if(TitleScreen && TitlePage==1) CancelJoinAction?.Invoke();
        FinishEditing(); Open=true;TitleScreen=true;SettingsFromTitle=false;
        TitlePage=0;TitleSelected=0;hoveredItem=-1;Dirty=true;
    }
    internal void LeaveTitle()
    {
        FinishEditing(); Open=false;TitleScreen=false;SettingsFromTitle=false;Dirty=true;
    }
    internal void ShowJoinScreen()
    {
        FinishEditing();TitleScreen=true;TitlePage=1;TitleSelected=0;Dirty=true;
    }
    internal void ShowSettingsFromTitle()
    {
        FinishEditing();TitleScreen=false;SettingsFromTitle=true;OpenCategories();
    }
    internal void SetPage(int page)
    {
        if(page is 0 or 2 or 4 or 8 || page<0 || page>=Labels.Length) return;
        if(page!=9 && View!=MenuView.Settings)
            settingsReturn=View==MenuView.Pause?MenuView.Pause:MenuView.Categories;
        FinishEditing(); Page=Math.Clamp(page,0,12); Selected=0;ScrollRow=0;
        View=Page==9?MenuView.Pause:MenuView.Settings;
        hoveredItem=-1;Dirty=true;InteractionSound?.Invoke();
    }
    internal void CyclePage(int step)
    {
        if(View!=MenuView.Settings) return;
        int index=Array.FindIndex(Tabs,tab=>tab.Page==Page);
        SetPage(Tabs[(index+step+Tabs.Length)%Tabs.Length].Page);
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
    internal void EditNameCharacter(char c)
    {
        c=char.ToUpperInvariant(c);
        if(EditingName && (char.IsAsciiLetterOrDigit(c)||c==' '||c=='-') && PlayerName.Length<12)
        { PlayerName+=c; Dirty=true; InteractionSound?.Invoke(); }
    }
    internal void BackspaceName() { if(EditingName && PlayerName.Length>0) { PlayerName=PlayerName[..^1];Dirty=true;InteractionSound?.Invoke(); } }
    internal void FinishName() { EditingName=false;PlayerName=PlayerName.Trim();if(PlayerName.Length==0) PlayerName="BEAN";Dirty=true;InteractionSound?.Invoke(); }
    internal void FinishEditing()
    {
        if(EditingName) FinishName();
        EditingJoinKey=false;
        JoinKey=JoinKey.Trim();
        Dirty=true;
    }
    internal void EditConnectionCharacter(char c)
    {
        if(c<32 || c>126) return;
        if(EditingJoinKey && JoinKey.Length<128) JoinKey+=c;
        else return;
        Dirty=true;
    }
    internal void PasteConnectionText(string text)
    {
        foreach(char c in text) EditConnectionCharacter(c);
    }
    internal void BackspaceConnection()
    {
        if(EditingJoinKey && JoinKey.Length>0) JoinKey=JoinKey[..^1];
        Dirty=true;
    }
    internal void ClearConnectionField()
    {
        if(EditingJoinKey) JoinKey="";
        Dirty=true;
    }
    internal void Select(int step)
    {
        if(TitleScreen)
        {
            int next=Math.Clamp(TitleSelected+step,0,TitlePage==0?3:2);
            if(next!=TitleSelected) InteractionSound?.Invoke();
            TitleSelected=next;Dirty=true;return;
        }
        if(View==MenuView.Categories)
        { CategorySelected=Math.Clamp(CategorySelected+step,0,Tabs.Length-1);Dirty=true;InteractionSound?.Invoke();return; }
        if(View==MenuView.Help) return;
        int selected=Math.Clamp(Selected + step, 0, Labels[Page].Length - 1);
        if(selected!=Selected) InteractionSound?.Invoke(); Selected=selected; Dirty=true;
        EnsureVisible();
    }
    void EnsureVisible()
    {
        if(View!=MenuView.Settings) return;
        if(Selected<ScrollRow) ScrollRow=Selected;
        else if(Selected>=ScrollRow+VisibleRows) ScrollRow=Selected-VisibleRows+1;
    }
    const int VisibleRows=9;
    const int PauseRowTop=80, PauseRowPitch=15;
    const int CategoryRowTop=85, CategoryRowPitch=21;
    const int SettingsRowTop=80, SettingsRowPitch=12;
    const int TitleRowTop=91, TitleRowPitch=25;
    static int RowAt(int x,int y,int first,int pitch,int count)
    {
        if(x<27 || x>=293 || y<first-6 || y>=first-6+count*pitch) return -1;
        return (y-(first-6))/pitch;
    }
    int CategoryAt(int x,int y) => RowAt(x,y,CategoryRowTop,CategoryRowPitch,Tabs.Length);
    int PauseAt(int x,int y) => RowAt(x,y,PauseRowTop,PauseRowPitch,Labels[9].Length);
    int SettingsAt(int x,int y)
    {
        int slot=RowAt(x,y,SettingsRowTop,SettingsRowPitch,VisibleRows);
        int row=slot<0?-1:ScrollRow+slot;
        return row<Labels[Page].Length?row:-1;
    }
    int TitleRowAt(int x,int y) => RowAt(x,y,TitleRowTop,TitleRowPitch,TitlePage==0?4:3);
    internal void AdjustSelected(int direction)
    {
        if(TitleScreen) ActivateTitleSelection();
        else if(View==MenuView.Categories) SetPage(Tabs[CategorySelected].Page);
        else if(View==MenuView.Help) Back();
        else Adjust(Selected,direction);
    }
    void ActivateTitleSelection()
    {
        InteractionSound?.Invoke();
        if(TitlePage==0)
        {
            switch(TitleSelected)
            {
                case 1: ShowJoinScreen();break;
                case 2: ShowSettingsFromTitle();break;
                default: TitleAction?.Invoke(TitleSelected);break;
            }
        }
        else switch(TitleSelected)
        {
            case 0: FinishEditing();EditingJoinKey=true;JoinKey="";break;
            case 1: FinishEditing();ConnectAction?.Invoke();break;
            case 2: ShowTitle();break;
        }
        Dirty=true;
    }
    internal void MouseDown(int x,int y)
    {
        if(!Open) return;
        if(TitleScreen)
        {
            int row=TitleRowAt(x,y);
            if(row>=0) { TitleSelected=row;ActivateTitleSelection(); }
            return;
        }
        if(View==MenuView.Help) { Back();return; }
        if(View==MenuView.Categories)
        {
            int row=CategoryAt(x,y);
            if(row>=0) { CategorySelected=row;SetPage(Tabs[row].Page); }
            return;
        }
        int selected=View==MenuView.Pause?PauseAt(x,y):SettingsAt(x,y);
        if(selected<0) return;
        Selected=selected;Dirty=true;
        if(View==MenuView.Pause) Adjust(selected,1);
        else Adjust(selected,x>170 && x<205?-1:1);
    }
    internal void MouseMove(int x,int y)
    {
        if(!Open) return;
        int row=TitleScreen?TitleRowAt(x,y):View==MenuView.Categories?CategoryAt(x,y):
            View==MenuView.Pause?PauseAt(x,y):View==MenuView.Settings?SettingsAt(x,y):-1;
        if(row<0 || row==hoveredItem) return;
        hoveredItem=row;
        if(TitleScreen) TitleSelected=row;
        else if(View==MenuView.Categories) CategorySelected=row;
        else Selected=row;
        Dirty=true;HoverSound?.Invoke();
    }
    internal void MouseUp() { }
    internal void MouseWheel(int delta)
    {
        if(View==MenuView.Settings)
        { ScrollRow=Math.Clamp(ScrollRow-Math.Sign(delta),0,Math.Max(0,Labels[Page].Length-VisibleRows));Dirty=true; }
    }
}
