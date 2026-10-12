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
        const int width=260, height=180;
        int X(int x) => left+x-20;
        int Y(int y) => top+y-8;

        void SmallText(string text,Brush brush,int x,int y)
        {
            foreach(char c in text.ToUpperInvariant())
            {
                if(Glyphs.TryGetValue(c,out var rows))
                    for(int yy=0;yy<6;yy++) for(int xx=0;xx<4;xx++)
                    {
                        int sourceY=yy switch { 0=>0, 1=>1, 2=>3, 3=>4, 4=>5, _=>6 };
                        int sourceX=xx switch { 0=>0, 1=>1, 2=>3, _=>4 };
                        if((rows[sourceY]&(1<<(4-sourceX)))!=0) g.FillRectangle(brush,x+xx,y+yy,1,1);
                    }
                x+=5;
            }
        }

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
            SmallText(title,whiteText,left+8,top+7);
            if(reset) Button("RESET",X(218),Y(12),36,12,false);
            if(back) Button("X",X(260),Y(12),14,12,false);
        }
        void Button(string text,int x,int y,int w,int h,bool selected)
        {
            Bevel(x,y,w,h,!selected);
            SmallText(text,black,x+(w-text.Length*5)/2,y+(h-6)/2);
            if(selected)
                for(int px=x+4;px<x+w-4;px+=2) g.FillRectangle(black,px,y+4,1,1);
        }
        void Row(string label,string value,int y,bool selected,bool locked=false,bool button=false)
        {
            if(selected) g.FillRectangle(blue,X(43),y-2,234,12);
            Brush ink=selected?whiteText:locked?disabled:black;
            SmallText((selected?"> ":"  ")+label,ink,X(48),y);
            if(value.Length>0)
            {
                if(button)
                {
                    int buttonWidth=Math.Clamp(value.Length*5+12,39,94);
                    Button(value,X(272)-buttonWidth,y-3,buttonWidth,13,selected);
                }
                else SmallText(value,ink,X(272)-value.Length*5,y);
            }
        }
        void Footer(string text) => SmallText(text,black,X(39),Y(169));

        if(TitleScreen)
        {
            if(TitlePage==0)
            {
                Window("BEANROOMS");
                SmallText("SELECT MODE",black,X(52),Y(43));
                string[] choices=["PLAY SOLO","JOIN ONLINE","SETTINGS","EXIT GAME"];
                for(int row=0;row<choices.Length;row++)
                {
                    int y=TitleRowTop+row*TitleRowPitch;
                    Button(choices[row],X(52),y-4,196,16,row==TitleSelected);
                }
                Footer("ARROWS / ENTER");
            }
            else
            {
                Window("BEANROOMS - ONLINE");
                SmallText("ROOM NAME",black,X(52),Y(43));
                bool fieldSelected=TitleSelected==0;
                Bevel(X(52),Y(58),216,28,!fieldSelected);
                if(fieldSelected) g.FillRectangle(blue,X(54),Y(60),212,24);
                string room=JoinKey.Length==0?"ENTER ROOM NAME":JoinKey.ToUpperInvariant();
                if(room.Length>30) room=room[..30];
                SmallText(room+(EditingJoinKey?"_":""),fieldSelected?whiteText:black,X(58),Y(68));
                Button("CONNECT",X(52),Y(108),92,16,TitleSelected==1);
                Button("BACK",X(156),Y(108),92,16,TitleSelected==2);
                SmallText("STATUS: "+(NetworkConnected?"ONLINE":NetworkStatus),black,X(52),Y(140));
                Footer("ENTER EDIT / CONNECT");
            }
            return;
        }

        if(View==MenuView.Pause)
        {
            Window("BEANROOMS - PAUSE");
            SmallText(NetworkConnected?$"ONLINE SESSION / {NetworkPlayers} PLAYERS":"SOLO SESSION",black,X(48),Y(35));
            Button("RESUME",X(48),Y(46),180,16,Selected==0);
            Button("PROFILE",X(48),Y(68),86,16,Selected==1);
            Button("SETTINGS",X(142),Y(68),86,16,Selected==2);
            Button("DEVELOPER",X(48),Y(90),86,16,Selected==3);
            Button("CONTROLS",X(142),Y(90),86,16,Selected==4);
            Button("LEAVE SESSION",X(48),Y(118),180,16,Selected==5);
            Button("QUIT GAME",X(48),Y(140),180,16,Selected==6);
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
                Button(Tabs[row].Name,X(48),y-4,180,14,row==CategorySelected);
                if((Tabs[row].Page==5&&!MonsterControlsWritable) ||
                   (Tabs[row].Page==8&&!FlickerControlsWritable))
                    SmallText("HOST",disabled,X(204),y);
                else if(locked) SmallText("SOLO",disabled,X(204),y);
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
            for(int row=0;row<help.Length;row++) SmallText(help[row],black,X(48),Y(42)+row*15);
            Footer("ESC BACK");
            return;
        }

        string title=Tabs.First(tab=>tab.Page==Page).Name;
        Window(title,reset:true,back:true);
        if(ScrollRow>0) SmallText("^",black,X(272),Y(31));
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
                SmallText(value,selected?whiteText:locked?disabled:black,X(272)-value.Length*5,y);
            }
        }
        if(ScrollRow+VisibleRows<Labels[Page].Length) SmallText("V",black,X(272),Y(157));
        Footer("Q/E PAGE     ESC BACK");
    }
}
