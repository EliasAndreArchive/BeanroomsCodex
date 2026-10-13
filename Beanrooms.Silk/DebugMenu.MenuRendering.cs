using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    // The same 5x7 glyphs used by diagnostics draw every menu label.
    void DrawMenu(Graphics g)
    {
        using var backdrop=new SolidBrush(Color.FromArgb(TitleScreen || SettingsFromTitle ? 255 : 222,7,17,17));
        using var panel=new SolidBrush(Color.FromArgb(190,11,29,28));
        using var selection=new SolidBrush(Color.FromArgb(125,45,103,84));
        using var ink=new SolidBrush(Color.FromArgb(209,231,212));
        using var muted=new SolidBrush(Color.FromArgb(111,151,136));
        using var accent=new SolidBrush(Color.FromArgb(145,244,191));
        using var red=new SolidBrush(Color.FromArgb(247,78,70));
        using var line=new SolidBrush(Color.FromArgb(68,112,93));
        g.FillRectangle(backdrop,0,0,320,240);
        for(int y=0;y<240;y+=4) g.FillRectangle(panel,0,y,320,1);
        g.FillRectangle(line,13,12,22,1);g.FillRectangle(line,13,12,1,14);
        g.FillRectangle(line,285,12,22,1);g.FillRectangle(line,306,12,1,14);
        g.FillRectangle(line,13,214,1,14);g.FillRectangle(line,13,227,22,1);
        g.FillRectangle(line,306,214,1,14);g.FillRectangle(line,285,227,22,1);
        g.FillRectangle(red,25,23,5,5);
        PixelText(g,TitleScreen || SettingsFromTitle?"STANDBY":"REC",red,35,22);
        PixelText(g,"CAM 01",muted,256,22);
        g.FillRectangle(line,25,35,270,1);
        g.FillRectangle(line,25,207,270,1);
        PixelText(g,TitleScreen || SettingsFromTitle?"NO SIGNAL  /  NO PLAYER":
            NetworkConnected?"LIVE FEED  /  ONLINE":"LIVE FEED  /  SOLO",muted,25,215);
        PixelText(g,"BAT III",muted,248,215);

        void Header(string title,string detail)
        {
            PixelText(g,title,accent,29,47);
            PixelText(g,detail,muted,29,59);
            g.FillRectangle(line,29,70,262,1);
        }
        void Item(string label,int y,bool selected,string value="")
        {
            if(selected)
            {
                g.FillRectangle(selection,27,y-5,266,17);
                g.FillRectangle(accent,27,y-5,2,17);
            }
            PixelText(g,(selected?"> ":"  ")+label,selected?accent:ink,34,y);
            if(value.Length>0)
            {
                int valueX=Math.Max(177,284-value.Length*6);
                PixelText(g,value,selected?accent:muted,valueX,y);
            }
        }
        void Footer(string hint) => PixelText(g,hint,muted,29,193);

        if(TitleScreen)
        {
            Header(TitlePage==0?"BEANROOMS / MAIN MENU":"BEANROOMS / ONLINE",
                TitlePage==0?"ARCHIVE 01  /  SELECT SESSION":"ROOM CHANNEL  /  CONNECTION");
            if(TitlePage==0)
            {
                string[] items=["PLAY SOLO","JOIN ONLINE","SETTINGS","EXIT GAME"];
                for(int i=0;i<items.Length;i++) Item(items[i],TitleRowTop+i*TitleRowPitch,TitleSelected==i,
                    i==0?"LOCAL":i==1?"ROOM":"");
                Footer("UP DOWN SELECT  /  ENTER OPEN");
            }
            else
            {
                string room=JoinKey.Length==0?"ENTER ROOM NAME":JoinKey.ToUpperInvariant();
                if(room.Length>17) room=room[..17];
                Item("ROOM",TitleRowTop,TitleSelected==0,room+(EditingJoinKey?"_":""));
                Item("CONNECT",TitleRowTop+TitleRowPitch,TitleSelected==1);
                Item("BACK",TitleRowTop+2*TitleRowPitch,TitleSelected==2);
                PixelText(g,"STATUS "+(NetworkConnected?"ONLINE":NetworkStatus),muted,34,166);
                Footer("ENTER EDIT  /  ESC BACK");
            }
            return;
        }

        if(View==MenuView.Pause)
        {
            Header("BEANROOMS / PAUSED",NetworkConnected?
                $"ONLINE  /  {NetworkPlayers} PLAYERS":"SOLO SESSION  /  FEED HELD");
            for(int i=0;i<Labels[9].Length;i++)
                Item(Labels[9][i],PauseRowTop+i*PauseRowPitch,Selected==i);
            Footer("TAB RESUME  /  ESC RESUME");
            return;
        }
        if(View==MenuView.Categories)
        {
            Header(developerCategories?"SYSTEM / DEVELOPER":"CAMERA / SETTINGS",
                developerCategories?"ADVANCED CONTROLS":"VIEW  /  PROFILE  /  HUD");
            for(int i=0;i<Tabs.Length;i++)
                Item(Tabs[i].Name,CategoryRowTop+i*CategoryRowPitch,CategorySelected==i,
                    Tabs[i].Page==5&&!MonsterControlsWritable?"HOST":"");
            Footer("ENTER OPEN  /  ESC BACK");
            return;
        }
        if(View==MenuView.Help)
        {
            Header("CAMERA / CONTROLS","ON SCREEN KEY GUIDE");
            string[] help=[
                "WASD MOVE     SHIFT SPRINT", "C CROUCH      MOUSE LOOK",
                "LMB GRAB      WHEEL DISTANCE", "RMB ROTATE HELD OBJECT",
                "TAB PAUSE     ESC RELEASE", "F11 FULLSCREEN",
                "Q E SETTINGS PAGES", "DISPLAY SELECTS RENDERER"
            ];
            for(int i=0;i<help.Length;i++) PixelText(g,help[i],ink,35,78+i*13);
            Footer("ESC BACK");
            return;
        }

        string pageTitle=Tabs.First(tab=>tab.Page==Page).Name;
        Header((developerCategories?"SYSTEM / ":"CAMERA / ")+pageTitle,
            "Q E PAGE  /  LEFT RIGHT ADJUST");
        for(int slot=0;slot<VisibleRows;slot++)
        {
            int row=ScrollRow+slot;
            if(row>=Labels[Page].Length) break;
            string value=Value(row);
            if(IsChoice(row)) value="< "+value+" >";
            Item(Labels[Page][row],SettingsRowTop+slot*SettingsRowPitch,row==Selected,value);
        }
        if(ScrollRow>0) PixelText(g,"^",accent,284,77);
        if(ScrollRow+VisibleRows<Labels[Page].Length) PixelText(g,"V",accent,284,181);
        Footer("ESC BACK  /  R RESET PAGE");
    }
}
