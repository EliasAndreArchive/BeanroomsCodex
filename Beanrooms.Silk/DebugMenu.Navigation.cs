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
        if(Open) { View=MenuView.Pause;Page=9;Selected=0;parentWindow=ParentWindowKind.None; }
        dragging=-1;hoveredItem=-1;Dirty=true;InteractionSound?.Invoke();
    }
    internal void Back()
    {
        FinishEditing();
        if(TitleScreen) { if(TitlePage==1) ShowTitle();return; }
        if(View==MenuView.Settings)
        {
            View=settingsReturn;
            parentWindow=settingsReturn==MenuView.Categories?ParentWindowKind.Pause:ParentWindowKind.None;
            WindowX=Math.Max(0,WindowX-10);WindowY=Math.Max(0,WindowY-8);
            Dirty=true;return;
        }
        if(View is MenuView.Categories or MenuView.Help)
        { if(SettingsFromTitle) ShowTitle(); else { View=MenuView.Pause;Page=9;Selected=0;parentWindow=ParentWindowKind.None;Dirty=true; } return; }
        Toggle();
    }
    void PushParent(ParentWindowKind kind)
    {
        parentWindow=kind;parentWindowX=WindowX;parentWindowY=WindowY;
        WindowX=Math.Clamp(WindowX+10,0,320-MenuWidth);
        WindowY=Math.Clamp(WindowY+8,0,240-MenuHeight);
    }
    internal void OpenCategories(bool developer=false)
    {
        FinishEditing();
        if(!TitleScreen && !SettingsFromTitle && View==MenuView.Pause) PushParent(ParentWindowKind.Pause);
        TitleScreen=false;developerCategories=developer;View=MenuView.Categories;CategorySelected=0;Dirty=true;InteractionSound?.Invoke();
    }
    internal void OpenHelp()
    {
        FinishEditing();
        if(!TitleScreen && View==MenuView.Pause) PushParent(ParentWindowKind.Pause);
        TitleScreen=false;View=MenuView.Help;Dirty=true;InteractionSound?.Invoke();
    }
    internal void ShowTitle()
    {
        if(TitleScreen && TitlePage==1) CancelJoinAction?.Invoke();
        FinishEditing(); Open=true;TitleScreen=true;SettingsFromTitle=false;parentWindow=ParentWindowKind.None;
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
        if(page!=9 && View==MenuView.Categories) PushParent(ParentWindowKind.Categories);
        FinishEditing(); Page=Math.Clamp(page,0,12); Selected=0;ScrollRow=0;
        View=Page==9?MenuView.Pause:MenuView.Settings;
        dragging=-1;hoveredItem=-1;Dirty=true;InteractionSound?.Invoke();
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
        FinishEditing();dragging=-1;Dirty=true;
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
    const int MenuWidth=230, MenuHeight=160;
    int MenuLeft => WindowX;
    int MenuRight => WindowX+MenuWidth;
    int PauseRowTop => WindowY+38;
    const int PauseRowPitch=17;
    int CategoryRowTop => WindowY+35;
    const int CategoryRowPitch=15;
    int SettingsRowTop => WindowY+31;
    const int SettingsRowPitch=13;
    int TitleRowTop => WindowY+60;
    const int TitleRowPitch=20;
    static int RowAt(int y,int first,int pitch,int count)
    {
        int top=first-3;
        return y>=top && y<top+count*pitch ? (y-top)/pitch : -1;
    }
    int CategoryY(int index) => CategoryRowTop+index*CategoryRowPitch;
    int CategoryAt(int y) => RowAt(y,CategoryRowTop,CategoryRowPitch,Tabs.Length);
    int PauseAt(int x,int y)
    {
        int localX=x-WindowX,localY=y-WindowY;
        if(localX<28 || localX>=194) return -1;
        if(localY>=46 && localY<62) return 0;
        if(localY>=68 && localY<84) return localX<106?1:localX<194?2:-1;
        if(localY>=90 && localY<106) return localX<106?3:localX<194?4:-1;
        if(localY>=118 && localY<134) return 5;
        if(localY>=140 && localY<156) return 6;
        return -1;
    }
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

    internal void MouseDown(int x, int y)
    {
        int headerTop=WindowY+3;
        int headerBottom=WindowY+18;
        int resetLeft=WindowX+162;
        int closeLeft=WindowX+210;
        if(y>=headerTop && y<headerBottom && x>=closeLeft)
        {
            if(!TitleScreen) Back();
            return;
        }
        if(y>=headerTop && y<headerBottom && x>=resetLeft && View==MenuView.Settings && !TitleScreen)
        { ResetDefaults();InteractionSound?.Invoke();return; }
        if(y>=headerTop && y<headerBottom && x>=MenuLeft+3 && x<MenuRight-3)
        {
            movingWindow=true;windowGrabX=x-WindowX;windowGrabY=y-WindowY;
            Dirty=true;return;
        }
        if(TitleScreen)
        {
            int titleRow=TitleRowAt(x,y);
            if(titleRow>=0) { TitleSelected=titleRow;ActivateTitleSelection(); }
            return;
        }
        if(View==MenuView.Categories)
        {
            int category=CategoryAt(y);
            if(x>=MenuLeft && x<MenuRight && category>=0)
            { CategorySelected=category;SetPage(Tabs[category].Page); }
            return;
        }
        if(View==MenuView.Help) { Back();return; }
        int row=View==MenuView.Pause?PauseAt(x,y):
            RowAt(y,SettingsRowTop,SettingsRowPitch,VisibleRows);
        if(View==MenuView.Settings && row>=0) row+=ScrollRow;
        if(row<0 || row>=Labels[Page].Length || x<MenuLeft || x>=MenuRight) return;
        Selected = row;
        if(View==MenuView.Pause) { Adjust(row,1);return; }
        if (IsSlider(row) && x >= WindowX+120 && x <= WindowX+178)
        {
            dragging = row;
            SetSliderFromX(row,x);
        }
        else if(IsChoice(row) && x>=WindowX+212-(Value(row).Length+4)*5 &&
                x<WindowX+212-(Value(row).Length+1)*5) Adjust(row,-1);
        else Adjust(row,1);
        Dirty = true;
    }
    internal void MouseMove(int x,int y)
    {
        if(movingWindow)
        {
            WindowX=Math.Clamp(x-windowGrabX,0,320-MenuWidth);
            WindowY=Math.Clamp(y-windowGrabY,0,240-MenuHeight);
            Dirty=true;return;
        }
        if(TitleScreen)
        {
            int row=TitleRowAt(x,y);
            if(row>=0 && row!=TitleSelected)
            { TitleSelected=row;Dirty=true;HoverSound?.Invoke(); }
            return;
        }
        if(View==MenuView.Categories)
        {
            int row=CategoryAt(y);
            if(x>=MenuLeft && x<MenuRight && row>=0 && CategorySelected!=row)
            { CategorySelected=row;Dirty=true;HoverSound?.Invoke(); }
            return;
        }
        if(View==MenuView.Help) return;
        if (dragging < 0)
        {
            int hovered=-1;
            if(y>=WindowY+3 && y<WindowY+18 && x>=WindowX+162 && x<WindowX+210 && View==MenuView.Settings) hovered=109;
            else if(y>=WindowY+3 && y<WindowY+18 && x>=WindowX+210) hovered=110;
            else if(x>=MenuLeft && x<MenuRight)
            {
                int row=View==MenuView.Pause?PauseAt(x,y):
                    RowAt(y,SettingsRowTop,SettingsRowPitch,VisibleRows);
                if(View==MenuView.Settings && row>=0) row+=ScrollRow;
                if(row>=0 && row<Labels[Page].Length) hovered=row;
            }
            if(hovered!=hoveredItem)
            {
                hoveredItem=hovered;
                if(hovered>=0)
                {
                    if(hovered<100) Selected=hovered;
                    Dirty=true;
                    HoverSound?.Invoke();
                }
            }
            return;
        }
        SetSliderFromX(dragging,x);
    }
    internal void MouseUp() { dragging=-1;movingWindow=false; }
    internal void MouseWheel(int delta)
    {
        if(View==MenuView.Settings)
        { ScrollRow=Math.Clamp(ScrollRow-Math.Sign(delta),0,Math.Max(0,Labels[Page].Length-VisibleRows));Dirty=true; }
    }
    int TitleRowAt(int x,int y)
    {
        if(x<MenuLeft || x>=MenuRight) return -1;
        if(TitlePage==0)
            return RowAt(y,TitleRowTop,TitleRowPitch,4);
        if(y>=WindowY+53 && y<WindowY+88) return 0;
        if(y>=WindowY+100 && y<WindowY+124)
            return x<WindowX+120?1:2;
        return -1;
    }

}
