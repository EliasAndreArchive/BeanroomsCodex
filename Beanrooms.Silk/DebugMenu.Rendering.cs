using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    static void PixelText(Graphics g, string text, Brush brush, int x, int y)
    {
        DrawPixelText(g,text,Brushes.Black,x+1,y+1);
        DrawPixelText(g,text,brush,x,y);
    }
    static void DrawPixelText(Graphics g, string text, Brush brush, int x, int y)
    {
        foreach(char c in text.ToUpperInvariant())
        {
            if(Glyphs.TryGetValue(c,out var rows))
                for(int yy=0;yy<7;yy++) for(int xx=0;xx<5;xx++)
                    if((rows[yy] & (1 << (4-xx))) != 0) g.FillRectangle(brush,x+xx,y+yy,1,1);
            x += 6;
        }
    }

    static void DrawSelection(Graphics g,Brush accent,int y,int left=14)
    {
        g.FillRectangle(accent,left,y-2,2,11);
    }

    static void DrawDivider(Graphics g,Brush divider,int y,int left=14,int width=292)
        => g.FillRectangle(divider,left,y,width,1);

    static void DrawValueBox(Graphics g,Brush outline,int x,int y,int width=35)
    {
        g.FillRectangle(outline,x,y,width,1);
        g.FillRectangle(outline,x,y+11,width,1);
        g.FillRectangle(outline,x,y,1,12);
        g.FillRectangle(outline,x+width-1,y,1,12);
    }
    static void DrawFrameCorners(Graphics g,Brush brush,RectangleF bounds,int offset)
    {
        int left=(int)bounds.Left+offset,top=(int)bounds.Top+offset;
        int right=(int)bounds.Right+offset,bottom=(int)bounds.Bottom+offset;
        int corner=Math.Clamp(Math.Min(right-left,bottom-top)/4,2,4);
        g.FillRectangle(brush,left,top,corner,1);
        g.FillRectangle(brush,left,top,1,corner);
        g.FillRectangle(brush,right-corner,top,corner,1);
        g.FillRectangle(brush,right-1,top,1,corner);
        g.FillRectangle(brush,left,bottom-1,corner,1);
        g.FillRectangle(brush,left,bottom-corner,1,corner);
        g.FillRectangle(brush,right-corner,bottom-1,corner,1);
        g.FillRectangle(brush,right-1,bottom-corner,1,corner);
    }
    internal Bitmap Draw()
    {
        var image = new Bitmap(320, 240, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.None;
        g.Clear(Color.Transparent);
        using var white = new SolidBrush(Color.FromArgb(210, 210, 210));
        using var paleBlue = new SolidBrush(Color.FromArgb(175, 175, 175));
        using var green = new SolidBrush(Color.White);
        using var unfilled = new SolidBrush(Color.FromArgb(105, 105, 105));
        using var divider = new SolidBrush(Color.FromArgb(96, 96, 96));
        using var toggleOutline = new SolidBrush(Color.FromArgb(190, 190, 190));
        if(Open && TitleScreen)
        {
            PixelText(g,TitlePage==0?"BEANROOMS MENU":"BEANROOMS ONLINE",paleBlue,12,4);
            DrawDivider(g,divider,27);
            if(TitlePage==0)
            {
                PixelText(g,"SELECT MODE",white,44,43);
                string[] options=["PLAY SOLO","JOIN ONLINE","SETTINGS","EXIT"];
                for(int row=0;row<options.Length;row++)
                {
                    string label=options[row];
                    Brush ink=row==TitleSelected?green:white;
                    if(row==TitleSelected) DrawSelection(g,green,72+row*24,14);
                    PixelText(g,row==TitleSelected?">":" ",ink,21,72+row*24);
                    PixelText(g,label,ink,44,72+row*24);
                    DrawDivider(g,divider,84+row*24,20,280);
                }
                PixelText(g,"UP DOWN SELECT  ENTER CONFIRM",paleBlue,13,225);
            }
            else
            {
                if(TitleSelected==0) DrawSelection(g,green,70,14);
                if(TitleSelected==1) DrawSelection(g,green,125,14);
                if(TitleSelected==2) DrawSelection(g,green,160,14);
                PixelText(g,TitleSelected==0?">":" ",TitleSelected==0?green:white,21,70);
                PixelText(g,"PHOTON ROOM",TitleSelected==0?green:white,44,70);
                string masked=JoinKey.Length==0?"ENTER KEY":new string('*',Math.Min(JoinKey.Length,20));
                PixelText(g,masked+(EditingJoinKey?"-":""),TitleSelected==0?green:white,44,83);
                PixelText(g,TitleSelected==1?">":" ",TitleSelected==1?green:white,21,125);
                PixelText(g,"CONNECT",TitleSelected==1?green:white,44,125);
                PixelText(g,TitleSelected==2?">":" ",TitleSelected==2?green:white,21,160);
                PixelText(g,"BACK",TitleSelected==2?green:white,44,160);
                PixelText(g,"STATUS "+(NetworkConnected?"ONLINE":NetworkStatus),paleBlue,13,225);
            }
        }
        else if (Open)
        {
            if(View==MenuView.Pause)
            {
                PixelText(g,"PAUSED",paleBlue,13,8);
                PixelText(g,NetworkConnected?$"ONLINE {NetworkPlayers}P":"SOLO",paleBlue,230,8);
                DrawDivider(g,divider,27);
                for(int row=0;row<Labels[9].Length;row++)
                {
                    bool locked=row==5&&!NetworkConnected;
                    Brush ink=locked?unfilled:row==Selected?green:white;
                    int y=PauseRowTop+row*PauseRowPitch;
                    if(row==Selected) DrawSelection(g,green,y,18);
                    PixelText(g,row==Selected?">":" ",ink,26,y);
                    PixelText(g,Labels[9][row],ink,50,y);
                    if(row<Labels[9].Length-1) DrawDivider(g,divider,y+14,25,270);
                }
                PixelText(g,"TAB RESUME  ENTER SELECT",paleBlue,13,225);
            }
            else if(View==MenuView.Categories)
            {
                PixelText(g,developerCategories?"DEVELOPER":"SETTINGS",paleBlue,13,8);
                PixelText(g,"<",white,310,8);
                DrawDivider(g,divider,27);
                for(int index=0;index<Tabs.Length;index++)
                {
                    int y=CategoryY(index);
                    Brush ink=Tabs[index].Page==2&&!MovementControlsWritable?unfilled:
                        index==CategorySelected?green:white;
                    if(index==CategorySelected) DrawSelection(g,green,y,18);
                    PixelText(g,index==CategorySelected?">":" ",ink,25,y);
                    PixelText(g,Tabs[index].Name,ink,50,y);
                    PixelText(g,">",ink,301,y);
                    if((Tabs[index].Page==5&&!MonsterControlsWritable) ||
                       (Tabs[index].Page==8&&!FlickerControlsWritable))
                        PixelText(g,"HOST",paleBlue,266,y);
                    if(index!=Tabs.Length-1) DrawDivider(g,divider,y+11,24,276);
                }
                PixelText(g,"UP DOWN  ENTER OPEN  ESC BACK",paleBlue,13,225);
            }
            else if(View==MenuView.Help)
            {
                PixelText(g,"CONTROLS",paleBlue,13,8);
                PixelText(g,"<",white,310,8);
                DrawDivider(g,divider,27);
                string[] help=
                [
                    "WASD MOVE   SHIFT SPRINT", "C CROUCH   MOUSE LOOK",
                    "LMB HOLD   WHEEL DISTANCE", "RMB + MOUSE ROTATE HELD",
                    "TAB PAUSE   ESC RELEASE", "F11 FULLSCREEN",
                    "Q E CHANGE MENU PAGE",
                    "ARROWS ADJUST SETTINGS"
                ];
                for(int i=0;i<help.Length;i++) PixelText(g,help[i],white,20,41+i*17);
                DrawDivider(g,divider,91);
                DrawDivider(g,divider,159);
                PixelText(g,"ESC BACK",paleBlue,13,225);
            }
            else
            {
                string heading=Tabs.First(tab=>tab.Page==Page).Name;
                PixelText(g,heading,paleBlue,13,8);
                PixelText(g,"RESET",white,270,8);
                PixelText(g,"<",white,310,8);
                DrawDivider(g,divider,27);
                if(ScrollRow>0) PixelText(g,"^",paleBlue,306,31);
                for(int slot=0;slot<VisibleRows;slot++)
                {
                    int row=ScrollRow+slot;
                    if(row>=Labels[Page].Length) break;
                    int y=SettingsRowTop+slot*RowPitch;
                    bool locked=Page==5&&!MonsterControlsWritable ||
                        Page==8&&!FlickerControlsWritable ||
                        Page==2&&!MovementControlsWritable ||
                        Page==6&&row==8&&!NetworkConnected;
                    Brush ink=locked?unfilled:row==Selected?green:white;
                    if(row==Selected) DrawSelection(g,green,y,12);
                    PixelText(g,row==Selected?">":" ",ink,18,y);
                    PixelText(g,Labels[Page][row],ink,44,y);
                    if(IsSlider(row))
                    {
                        int filled=(int)MathF.Round(Math.Clamp(Fraction(row),0f,1f)*20f);
                        for(int tick=0;tick<20;tick++)
                            g.FillRectangle(tick<filled?ink:unfilled,155+tick*5,y+3,4,4);
                    }
                    string value=Value(row);
                    if(IsChoice(row)) value="< "+value+" >";
                    if(value is "ON" or "OFF")
                    {
                        DrawValueBox(g,toggleOutline,276,y-3,33);
                        PixelText(g,value,ink,292-value.Length*3,y);
                    }
                    else
                    {
                        bool action=Page==6 && row is (7 or 8);
                        if(action) value=value+" >";
                        PixelText(g,value,ink,316-value.Length*6,y);
                    }
                    if(slot<VisibleRows-1) DrawDivider(g,divider,y+12,18,284);
                }
                if(ScrollRow+VisibleRows<Labels[Page].Length) PixelText(g,"V",paleBlue,306,210);
                PixelText(g,"ESC BACK  Q/E PAGES",paleBlue,13,225);
                if(Page==5&&!MonsterControlsWritable || Page==8&&!FlickerControlsWritable)
                    PixelText(g,"HOST ONLY",paleBlue,252,225);
                if(Page==2&&!MovementControlsWritable) PixelText(g,"SOLO ONLY",paleBlue,252,225);
            }
            DrawDivider(g,divider,218);
        }
        else
        {
            if(ShowFps) PixelText(g,$"FPS {Math.Clamp(FpsValue,0,999):000}",white,13,10);
            if(ShowPlayerTags) foreach(var tag in PlayerTags)
            {
                using var brush=new SolidBrush(tag.Ink);
                PixelText(g,tag.Label,brush,(int)tag.At.X-tag.Label.Length*3,(int)tag.At.Y);
            }
            if(ShowObjectFrames) foreach(var frame in ObjectFrames)
            {
                DrawFrameCorners(g,Brushes.Black,frame,1);
                DrawFrameCorners(g,white,frame,0);
                if(ObjectPrompt.Length>0)
                    PixelText(g,ObjectPrompt,white,Math.Clamp((int)frame.Left,5,315-ObjectPrompt.Length*6),
                        Math.Clamp((int)frame.Bottom+3,10,211));
            }
            if(ShowDebugOverlay)
            {
                for(int line=0;line<DebugLines.Length;line++)
                    PixelText(g,DebugLines[line],line==0?green:line==1?paleBlue:white,13,10+line*12);
                PixelText(g,MonsterTargetText,paleBlue,190,10);
            }
            if(ShowDebugOverlay && MonsterDebug)
            {
                foreach(var segment in MonsterSegments)
                {
                    using var pen=new Pen(segment.Ink);
                    g.DrawLine(pen,segment.A,segment.B);
                }
                foreach(var point in MonsterPoints)
                {
                    using var brush=new SolidBrush(point.Ink);
                    g.FillRectangle(brush,point.At.X-2,point.At.Y-2,5,5);
                    PixelText(g,point.Label,brush,(int)point.At.X+4,(int)point.At.Y-3);
                }
            }
        }
        Dirty = false;
        return image;
    }
}
