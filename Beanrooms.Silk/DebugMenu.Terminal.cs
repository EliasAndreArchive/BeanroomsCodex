using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    sealed class TerminalLine
    {
        internal string Text;
        internal int Visible;
        internal string Action="";
        internal int SettingPage=-1, SettingRow=-1;
        internal int SliderStart=-1, SliderEnd=-1;
        internal bool Error;
        internal int InputStart=-1;
        internal List<(int Start,int Length,Color Ink)>? ColorSpans;
        internal TerminalLine(string text) { Text=text; }
    }

    const int TerminalVisibleRows=17;
    const int TerminalRowHeight=12, TerminalTop=10;
    readonly List<TerminalLine> terminalLines=[];
    int currentMenuStart,currentMenuEnd;
    int terminalScroll;
    float revealBudget;
    internal IReadOnlyList<string> TerminalHistory => terminalLines.Select(line=>line.Text).ToArray();
    internal int TerminalScroll => terminalScroll;
    internal int RevealedCharacters => terminalLines.Sum(line=>line.Visible);

    string MenuPath => TitleScreen?(TitlePage==0?"/BEANROOMS/MAINMENU":"/BEANROOMS/ONLINE"):
        View==MenuView.Pause?"/BEANROOMS/PAUSE":
        View==MenuView.Categories?developerCategories?"/BEANROOMS/DEVELOPER":"/BEANROOMS/SETTINGS":
        View==MenuView.Help?"/BEANROOMS/HELP":
        View==MenuView.Network?networkPlayersOpen?"/BEANROOMS/NETWORK/PLAYERS":"/BEANROOMS/NETWORK":
        "/BEANROOMS/"+Tabs.First(tab=>tab.Page==Page).Name.Replace(' ','_');

    internal void ResetTerminal()
    {
        terminalLines.Clear();
        currentMenuStart=currentMenuEnd=0;
        terminalScroll=0;
        revealBudget=0;
        Dirty=true;
    }

    void PrintLine(string line="",string action="",int settingRow=-1,int sliderStart=-1,int sliderEnd=-1,
        bool error=false,int inputStart=-1)
    {
        if(line.Length==0)
        {
            terminalLines.Add(new TerminalLine(""));
            if(terminalScroll>0) terminalScroll++;
        }
        else
        {
            for(int start=0;start<line.Length;start+=49)
            {
                var entry=new TerminalLine(line.Substring(start,Math.Min(49,line.Length-start)));
                entry.Error=error;
                if(inputStart>=0 && inputStart<start+entry.Text.Length)
                    entry.InputStart=Math.Max(0,inputStart-start);
                if(start==0)
                {
                    entry.Action=action;
                    entry.SettingPage=settingRow<0?-1:Page;
                    entry.SettingRow=settingRow;
                    entry.SliderStart=sliderStart;
                    entry.SliderEnd=sliderEnd;
                }
                terminalLines.Add(entry);
                if(terminalScroll>0) terminalScroll++;
            }
        }
        if(terminalLines.Count>500)
        {
            int excess=terminalLines.Count-500;
            terminalLines.RemoveRange(0,excess);
            terminalScroll=Math.Max(0,terminalScroll-excess);
            currentMenuStart=Math.Max(0,currentMenuStart-excess);
            currentMenuEnd=Math.Max(0,currentMenuEnd-excess);
        }
        Dirty=true;
    }

    internal void PrintCurrentView()
    {
        if(!Open) return;
        activeSliderRow=-1;
        foreach(TerminalLine line in terminalLines)
        { line.Action="";line.SettingPage=-1;line.SettingRow=-1; }
        int viewStart=terminalLines.Count;
        PrintLine();
        currentMenuStart=terminalLines.Count;
        PrintLine(MenuPath+"> DIR");
        if(View!=MenuView.Network) PrintLine();
        if(TitleScreen)
        {
            if(TitlePage==0)
            {
                PrintLine("SOLO",action:"SOLO");
                PrintLine("ONLINE",action:"ONLINE");
                PrintLine("SETTINGS",action:"SETTINGS");
                PrintLine("QUIT",action:"QUIT");
            }
            else
            {
                PrintLine("ROOM <NAME> = "+(JoinKey.Length==0?"NOT SET":JoinKey.ToUpperInvariant()),action:"ROOM ");
                PrintLine("CONNECT <ROOM NAME>",action:"CONNECT ");
                PrintLine("BACK",action:"BACK");
                PrintLine("STATUS = "+(NetworkConnected?"ONLINE":NetworkStatus));
            }
        }
        else if(View==MenuView.Pause)
        {
            string[] options=NetworkConnected
                ?["RESUME","PROFILE","NETWORK","SETTINGS","DEVELOPER","HELP","LEAVE","QUIT"]
                :["RESUME","PROFILE","SETTINGS","DEVELOPER","HELP","LEAVE","QUIT"];
            foreach(string option in options) PrintLine(option,action:option);
        }
        else if(View==MenuView.Network) PrintNetworkContents();
        else if(View==MenuView.Categories)
        {
            foreach(var tab in Tabs) PrintLine(tab.Name,action:tab.Name);
            PrintLine("BACK",action:"BACK");
        }
        else if(View==MenuView.Help)
        {
            string[] help=[
                "WASD MOVE   SHIFT SPRINT",
                "C CROUCH    MOUSE LOOK",
                "LMB GRAB    WHEEL DISTANCE",
                "RMB ROTATE HELD OBJECT",
                "TAB PAUSE   ESC RELEASE",
                "F11 FULLSCREEN",
                "DISPLAY / LIGHTING CHANGES RENDERER",
                "BACK"
            ];
            foreach(string line in help) PrintLine(line);
            terminalLines[^1].Action="BACK";
        }
        else
        {
            for(int i=0;i<Labels[Page].Length;i++)
            {
                var (line,start,end)=SettingLine(i);
                PrintLine(line,settingRow:i,sliderStart:start,sliderEnd:end);
            }
            PrintLine("RESET",action:"RESET");
            PrintLine("BACK",action:"BACK");
        }
        currentMenuEnd=terminalLines.Count;
        PrintLine();
        terminalScroll=Math.Max(0,terminalLines.Count-TerminalVisibleRows-viewStart);
        Dirty=true;
    }

    internal void ScrollTerminal(int lines)
    {
        terminalScroll=Math.Clamp(terminalScroll+lines,0,Math.Max(0,terminalLines.Count-TerminalVisibleRows));
        Dirty=true;
    }

    internal void RefreshSettingLines()
    {
        foreach(TerminalLine line in terminalLines)
        {
            if(line.SettingPage!=Page || line.SettingRow<0) continue;
            var (text,start,end)=SettingLine(line.SettingRow);
            line.Text=text;
            line.Visible=text.Length;
            line.SliderStart=start;
            line.SliderEnd=end;
        }
        Dirty=true;
    }

    internal void ClickTerminal(int x,int y)
    {
        if(!Open || x<10 || x>=310 || y<TerminalTop || y>=TerminalTop+TerminalVisibleRows*TerminalRowHeight) return;
        int slot=(y-TerminalTop)/TerminalRowHeight;
        int top=Math.Max(0,terminalLines.Count-TerminalVisibleRows-terminalScroll);
        if(slot>=TerminalVisibleRows || top+slot>=terminalLines.Count) return;
        TerminalLine line=terminalLines[top+slot];
        if(line.Visible==0) return;
        int column=(x-10)/6;
        if(line.SettingRow>=0 && line.SettingPage==Page && View==MenuView.Settings)
        {
            if(Page==5 && !MonsterControlsWritable) { PrintLine("ERROR: HOST ONLY",error:true);return; }
            if(Page==6 && line.SettingRow==0)
            { CommandLine="NAME ";Dirty=true;return; }
            Selected=line.SettingRow;
            if(line.SliderStart>=0 && column>=line.SliderStart && column<=line.SliderEnd)
            {
                activeSliderRow=line.SettingRow;
                DragTerminalSlider(x);
            }
            else
            {
                Adjust(line.SettingRow,1);
                RefreshSettingLines();
            }
            return;
        }
        if(line.Action.Length==0) return;
        if(line.Action.EndsWith(' '))
        { CommandLine=line.Action;Dirty=true;return; }
        CommandLine=line.Action;
        SubmitCommand();
    }

    int activeSliderRow=-1;
    internal void ReleaseTerminalSlider() => activeSliderRow=-1;
    internal void DragTerminalSlider(int x)
    {
        if(activeSliderRow<0) return;
        TerminalLine? line=terminalLines.LastOrDefault(item=>item.SettingPage==Page && item.SettingRow==activeSliderRow);
        if(line==null || line.SliderStart<0) return;
        float fraction=Math.Clamp((x-(10+line.SliderStart*6))/(float)Math.Max(1,(line.SliderEnd-line.SliderStart)*6),0,1);
        SetNumericFraction(activeSliderRow,fraction);
        RefreshSettingLines();
    }

    internal void FlushTerminal()
    {
        foreach(TerminalLine line in terminalLines) line.Visible=line.Text.Length;
        revealBudget=0;
        Dirty=true;
    }

    internal void ReportStatus(string status)
    {
        if(Open) PrintLine("STATUS = "+status);
    }

    internal void AdvanceTerminal(float dt)
    {
        if(!Open) return;
        revealBudget+=Math.Clamp(dt,0f,0.05f)*220f;
        int budget=(int)revealBudget;
        if(budget==0) return;
        revealBudget-=budget;
        foreach(TerminalLine line in terminalLines)
        {
            if(budget<=0) break;
            int remaining=line.Text.Length-line.Visible;
            if(remaining<=0) continue;
            int reveal=Math.Min(remaining,budget);
            if(OutputSound!=null)
                for(int i=0;i<reveal;i++) OutputSound();
            line.Visible+=reveal;
            budget-=reveal;
            Dirty=true;
        }
    }

    string PromptInput()
    {
        int available=Math.Max(5,49-MenuPath.Length-2);
        return (CommandLine.Length>available?"<"+CommandLine[^(available-1)..]:CommandLine)+"_";
    }

    void DrawTerminal(Graphics g,Brush white)
    {
        using var input=new SolidBrush(Color.FromArgb(255,222,158));
        using var error=new SolidBrush(Color.FromArgb(255,105,105));
        int top=Math.Max(0,terminalLines.Count-TerminalVisibleRows-terminalScroll);
        for(int i=0;i<TerminalVisibleRows && top+i<terminalLines.Count;i++)
        {
            TerminalLine line=terminalLines[top+i];
            if(line.Visible>0)
            {
                int y=TerminalTop+i*TerminalRowHeight;
                if(line.SettingPage==Page && line.SettingRow>=0 && line.SliderStart>=0 &&
                   View==MenuView.Settings && TryNumericControl(line.SettingRow,out NumericControl numeric))
                {
                    int left=10+line.SliderStart*6,right=10+line.SliderEnd*6;
                    float fraction=Math.Clamp((numeric.Current-numeric.Min)/(numeric.Max-numeric.Min),0,1);
                    int handle=left+(int)MathF.Round((right-left)*fraction);
                    using var track=new SolidBrush(Color.FromArgb(145,145,145,145));
                    using var fill=new SolidBrush(Color.FromArgb(210,210,210,210));
                    g.FillRectangle(Brushes.Black,left+1,y+5,right-left+1,1);
                    g.FillRectangle(track,left,y+4,right-left+1,1);
                    g.FillRectangle(fill,left,y+4,handle-left+1,1);
                    g.FillRectangle(Brushes.Black,handle+1,y+2,1,7);
                    g.FillRectangle(Brushes.Black,handle,y+2,4,1);
                    g.FillRectangle(Brushes.Black,handle,y+8,4,1);
                    g.FillRectangle(white,handle,y+1,1,7);
                    g.FillRectangle(white,handle-1,y+1,3,1);
                    g.FillRectangle(white,handle-1,y+7,3,1);
                }
                if(line.Error) PixelText(g,line.Text[..line.Visible],error,10,y);
                else if(line.ColorSpans is { Count: > 0 } spans)
                {
                    int cursor=0;
                    foreach(var span in spans)
                    {
                        if(span.Start>=line.Visible) break;
                        if(span.Start>cursor)
                            PixelText(g,line.Text[cursor..span.Start],white,10+cursor*6,y);
                        int end=Math.Min(line.Visible,span.Start+span.Length);
                        using var colored=new SolidBrush(span.Ink);
                        PixelText(g,line.Text[span.Start..end],colored,10+span.Start*6,y);
                        cursor=end;
                    }
                    if(cursor<line.Visible)
                        PixelText(g,line.Text[cursor..line.Visible],white,10+cursor*6,y);
                }
                else if(line.InputStart>=0 && line.InputStart<line.Visible)
                {
                    if(line.InputStart>0) PixelText(g,line.Text[..line.InputStart],white,10,y);
                    PixelText(g,line.Text[line.InputStart..line.Visible],input,10+line.InputStart*6,y);
                }
                else PixelText(g,line.Text[..line.Visible],white,10,y);
            }
        }
        string path=MenuPath+">";
        PixelText(g,path,white,10,220);
        PixelText(g,PromptInput(),input,10+path.Length*6,220);
    }
}
