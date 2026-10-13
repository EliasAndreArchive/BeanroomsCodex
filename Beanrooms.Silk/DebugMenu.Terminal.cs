using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    sealed class TerminalLine
    {
        internal readonly string Text;
        internal int Visible;
        internal TerminalLine(string text) { Text=text; }
    }

    const int TerminalVisibleRows=17;
    readonly List<TerminalLine> terminalLines=[];
    int terminalScroll;
    float revealBudget;
    internal IReadOnlyList<string> TerminalHistory => terminalLines.Select(line=>line.Text).ToArray();
    internal int TerminalScroll => terminalScroll;
    internal int RevealedCharacters => terminalLines.Sum(line=>line.Visible);

    string MenuPath => TitleScreen?(TitlePage==0?"/BEANROOMS/MAINMENU":"/BEANROOMS/ONLINE"):
        View==MenuView.Pause?"/BEANROOMS/PAUSE":
        View==MenuView.Categories?developerCategories?"/BEANROOMS/DEVELOPER":"/BEANROOMS/SETTINGS":
        View==MenuView.Help?"/BEANROOMS/HELP":
        View==MenuView.Spawn?"/BEANROOMS/SPAWN":
        "/BEANROOMS/"+Tabs.First(tab=>tab.Page==Page).Name.Replace(' ','_');

    internal void ResetTerminal()
    {
        terminalLines.Clear();
        terminalScroll=0;
        revealBudget=0;
        Dirty=true;
    }

    void PrintLine(string line="")
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
                terminalLines.Add(new TerminalLine(line.Substring(start,Math.Min(49,line.Length-start))));
                if(terminalScroll>0) terminalScroll++;
            }
        }
        if(terminalLines.Count>500)
        {
            int excess=terminalLines.Count-500;
            terminalLines.RemoveRange(0,excess);
            terminalScroll=Math.Max(0,terminalScroll-excess);
        }
        Dirty=true;
    }

    internal void PrintCurrentView()
    {
        if(!Open) return;
        PrintLine();
        PrintLine(MenuPath+"> DIR");
        PrintLine();
        if(TitleScreen)
        {
            if(TitlePage==0)
            {
                PrintLine("01. SOLO");
                PrintLine("02. ONLINE");
                PrintLine("03. SETTINGS");
                PrintLine("04. QUIT");
            }
            else
            {
                PrintLine("01. ROOM <NAME>  = "+(JoinKey.Length==0?"NOT SET":JoinKey.ToUpperInvariant()));
                PrintLine("02. CONNECT");
                PrintLine("03. BACK");
                PrintLine("STATUS = "+(NetworkConnected?"ONLINE":NetworkStatus));
            }
        }
        else if(View==MenuView.Pause)
        {
            string[] options=["RESUME","PROFILE","SETTINGS","DEVELOPER","SPAWN","HELP","LEAVE","QUIT"];
            for(int i=0;i<options.Length;i++) PrintLine($"{i+1:00}. {options[i]}");
        }
        else if(View==MenuView.Categories)
        {
            for(int i=0;i<Tabs.Length;i++) PrintLine($"{i+1:00}. {Tabs[i].Name}");
            PrintLine("TYPE BACK TO RETURN");
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
                "DISPLAY 1 SWITCHES RENDERER",
                "TYPE BACK TO RETURN"
            ];
            foreach(string line in help) PrintLine(line);
        }
        else if(View==MenuView.Spawn)
        {
            PrintLine("CUBE SIZE COLOR AMOUNT");
            PrintLine("GLOWCUBE SIZE COLOR AMOUNT");
            PrintLine();
            PrintLine("EXAMPLE  CUBE 1 WHITE 2");
            PrintLine("SIZE 0.25 TO 2.5  /  AMOUNT 1 TO 8");
            PrintLine("WHITE RED GREEN BLUE YELLOW");
            PrintLine("ORANGE PURPLE CYAN AMBER");
            PrintLine("TYPE BACK TO RETURN");
        }
        else
        {
            for(int i=ScrollRow;i<Math.Min(ScrollRow+VisibleRows,Labels[Page].Length);i++)
                PrintLine($"{i+1:00}. {Labels[Page][i]} = {Value(i)}");
            PrintLine("NUMBER [+/-]  /  SET N VALUE");
            if(Labels[Page].Length>VisibleRows)
                PrintLine($"ROWS {ScrollRow+1:00}-{Math.Min(ScrollRow+VisibleRows,Labels[Page].Length):00} / MORE LESS");
            PrintLine("NEXT PREV RESET BACK");
        }
        PrintLine();
    }

    internal void ScrollTerminal(int lines)
    {
        terminalScroll=Math.Clamp(terminalScroll+lines,0,Math.Max(0,terminalLines.Count-TerminalVisibleRows));
        Dirty=true;
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
            line.Visible+=reveal;
            budget-=reveal;
            Dirty=true;
        }
    }

    string PromptText()
    {
        int available=Math.Max(5,49-MenuPath.Length-2);
        string visible=CommandLine.Length>available?"<"+CommandLine[^(available-1)..]:CommandLine;
        return MenuPath+">"+visible+"_";
    }

    void DrawTerminal(Graphics g,Brush white)
    {
        int top=Math.Max(0,terminalLines.Count-TerminalVisibleRows-terminalScroll);
        for(int i=0;i<TerminalVisibleRows && top+i<terminalLines.Count;i++)
        {
            TerminalLine line=terminalLines[top+i];
            if(line.Visible>0)
                PixelText(g,line.Text[..line.Visible],white,10,10+i*12);
        }
        PixelText(g,PromptText(),white,10,220);
    }
}
