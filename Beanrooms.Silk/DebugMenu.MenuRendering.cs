using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    // Compact Windows 94/95-inspired dialogs at the game's native resolution.
    void DrawMenu(Graphics g)
    {
        using var window=new SolidBrush(Color.FromArgb(192,192,192));
        using var light=new SolidBrush(Color.White);
        using var shade=new SolidBrush(Color.FromArgb(128,128,128));
        using var dark=new SolidBrush(Color.FromArgb(64,64,64));
        using var blue=new SolidBrush(Color.FromArgb(0,0,128));
        using var whiteText=new SolidBrush(Color.White);
        using var black=new SolidBrush(Color.Black);
        using var disabled=new SolidBrush(Color.FromArgb(110,110,110));

        int left=WindowX, top=WindowY;
        const int width=280, height=200;
        int X(int x) => left+x-20;
        int Y(int y) => top+y-8;

        void Bevel(int x,int y,int w,int h,bool raised=true)
        {
            g.FillRectangle(black,x,y,w,h);
            g.FillRectangle(raised?light:dark,x+1,y+1,w-2,1);
            g.FillRectangle(raised?light:dark,x+1,y+1,1,h-2);
            g.FillRectangle(raised?dark:light,x+1,y+h-2,w-2,1);
            g.FillRectangle(raised?dark:light,x+w-2,y+1,1,h-2);
            g.FillRectangle(window,x+2,y+2,w-4,h-4);
        }
        void Window(string title,bool reset=false,bool back=false)
        {
            Bevel(left,top,width,height);
            g.FillRectangle(blue,left+3,top+3,width-6,15);
            DrawPixelText(g,title,whiteText,left+8,top+7);
            if(reset) Button("RESET",X(235),Y(12),42,13,false);
            if(back) Button("X",X(279),Y(12),14,13,false);
        }
        void Button(string text,int x,int y,int w,int h,bool selected)
        {
            Bevel(x,y,w,h,!selected);
            DrawPixelText(g,text,black,x+(w-text.Length*6)/2,y+(h-7)/2);
            if(selected)
                for(int px=x+4;px<x+w-4;px+=2) g.FillRectangle(black,px,y+4,1,1);
        }
        void Row(string label,string value,int y,bool selected,bool locked=false,bool button=false)
        {
            if(selected) g.FillRectangle(blue,X(43),y-2,234,12);
            Brush ink=selected?whiteText:locked?disabled:black;
            DrawPixelText(g,(selected?"> ":"  ")+label,ink,X(48),y);
            if(value.Length>0)
            {
                if(button) Button(value,X(234),y-3,39,13,selected);
                else DrawPixelText(g,value,ink,X(272)-value.Length*6,y);
            }
        }
        void Footer(string text) => DrawPixelText(g,text,black,X(39),Y(193));

        if(TitleScreen)
        {
            if(TitlePage==0)
            {
                Window("BEANROOMS");
                DrawPixelText(g,"SELECT MODE",black,X(52),Y(43));
                string[] choices=["PLAY SOLO","JOIN ONLINE","SETTINGS","EXIT GAME"];
                for(int row=0;row<choices.Length;row++)
                {
                    int y=TitleRowTop+row*TitleRowPitch;
                    Button(choices[row],X(52),y-5,216,18,row==TitleSelected);
                }
                Footer("ARROWS / ENTER");
            }
            else
            {
                Window("BEANROOMS - ONLINE");
                DrawPixelText(g,"ROOM NAME",black,X(52),Y(43));
                bool fieldSelected=TitleSelected==0;
                Bevel(X(52),Y(58),216,28,!fieldSelected);
                if(fieldSelected) g.FillRectangle(blue,X(54),Y(60),212,24);
                string room=JoinKey.Length==0?"ENTER ROOM NAME":JoinKey.ToUpperInvariant();
                if(room.Length>30) room=room[..30];
                DrawPixelText(g,room+(EditingJoinKey?"_":""),fieldSelected?whiteText:black,X(58),Y(68));
                Button("CONNECT",X(52),Y(108),102,18,TitleSelected==1);
                Button("BACK",X(166),Y(108),102,18,TitleSelected==2);
                DrawPixelText(g,"STATUS: "+(NetworkConnected?"ONLINE":NetworkStatus),black,X(52),Y(151));
                Footer("ENTER EDIT / CONNECT");
            }
            return;
        }

        if(View==MenuView.Pause)
        {
            Window("BEANROOMS - PAUSE");
            DrawPixelText(g,NetworkConnected?$"ONLINE  {NetworkPlayers} PLAYERS":"SOLO SESSION",black,X(48),Y(35));
            for(int row=0;row<Labels[9].Length;row++)
            {
                int y=PauseRowTop+row*PauseRowPitch;
                Row(Labels[9][row],Value(row),y,row==Selected,button:true);
            }
            Footer("TAB RESUME     ENTER SELECT");
            return;
        }

        if(View==MenuView.Categories)
        {
            Window(developerCategories?"DEVELOPER SETTINGS":"SETTINGS",back:true);
            for(int row=0;row<Tabs.Length;row++)
            {
                int y=CategoryY(row);
                bool locked=Tabs[row].Page==2&&!MovementControlsWritable;
                Row(Tabs[row].Name,"OPEN",y,row==CategorySelected,locked,true);
                if((Tabs[row].Page==5&&!MonsterControlsWritable) ||
                   (Tabs[row].Page==8&&!FlickerControlsWritable))
                    DrawPixelText(g,"HOST",disabled,X(201),y);
            }
            Footer("ARROWS / ENTER     ESC BACK");
            return;
        }

        if(View==MenuView.Help)
        {
            Window("CONTROLS",back:true);
            string[] help=
            [
                "WASD MOVE  SHIFT SPRINT", "C CROUCH  MOUSE LOOK",
                "LMB CARRY  WHEEL DIST", "RMB + MOUSE ROTATE",
                "TAB PAUSE  ESC RELEASE", "F11 FULLSCREEN",
                "Q E CHANGE MENU PAGE", "ARROWS ADJUST VALUES"
            ];
            for(int row=0;row<help.Length;row++) DrawPixelText(g,help[row],black,X(48),Y(44)+row*19);
            Footer("ESC BACK");
            return;
        }

        string title=Tabs.First(tab=>tab.Page==Page).Name;
        Window(title,reset:true,back:true);
        if(ScrollRow>0) DrawPixelText(g,"^",black,X(272),Y(31));
        for(int slot=0;slot<VisibleRows;slot++)
        {
            int row=ScrollRow+slot;
            if(row>=Labels[Page].Length) break;
            int y=SettingsRowTop+slot*SettingsRowPitch;
            bool selected=row==Selected;
            bool locked=Page==5&&!MonsterControlsWritable ||
                Page==8&&!FlickerControlsWritable ||
                Page==2&&!MovementControlsWritable;
            string value=Value(row);
            if(IsChoice(row)) value="< "+value+" >";
            Row(Labels[Page][row],IsSlider(row)?"":value,y,selected,locked,!IsSlider(row));
            if(IsSlider(row))
            {
                Bevel(X(158),y+1,72,8,false);
                int thumb=X(160)+(int)MathF.Round(Math.Clamp(Fraction(row),0f,1f)*66f);
                Bevel(thumb,y,5,10,true);
                DrawPixelText(g,value,selected?whiteText:locked?disabled:black,X(272)-value.Length*6,y);
            }
        }
        if(ScrollRow+VisibleRows<Labels[Page].Length) DrawPixelText(g,"V",black,X(272),Y(179));
        Footer("Q/E PAGE     ESC BACK");
    }
}
