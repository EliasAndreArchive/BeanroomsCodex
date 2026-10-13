using System.Globalization;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    internal void ClearCommand()
    {
        CommandLine="";
        CommandFeedback="";
        Dirty=true;
    }

    internal void TypeCommandCharacter(char c)
    {
        if(!Open || char.IsControl(c) || CommandLine.Length>=128) return;
        CommandLine+=char.ToUpperInvariant(c);
        CommandFeedback="";
        Dirty=true;
    }

    internal void PasteCommand(string text)
    {
        foreach(char c in text) TypeCommandCharacter(c);
    }

    internal void BackspaceCommand()
    {
        if(CommandLine.Length==0) return;
        CommandLine=CommandLine[..^1];
        CommandFeedback="";
        Dirty=true;
    }

    internal void SubmitCommand()
    {
        if(!Open) return;
        string command=CommandLine.Trim();
        CommandLine="";
        if(command.Length==0) { Dirty=true;return; }
        string upper=command.ToUpperInvariant();
        CommandFeedback="";
        FlushTerminal();
        bool scrolling=upper is "SCROLL UP" or "PAGE UP" or "SCROLL DOWN" or "PAGE DOWN" or "TOP" or "BOTTOM";
        if(!scrolling) ScrollTerminal(-1000);
        PrintLine(MenuPath+"> "+upper);
        int previousLines=terminalLines.Count;
        if(upper is "SCROLL UP" or "PAGE UP")
        { ScrollTerminal(10);return; }
        if(upper is "SCROLL DOWN" or "PAGE DOWN" or "BOTTOM")
        { ScrollTerminal(-1000);return; }
        if(upper=="TOP")
        { ScrollTerminal(1000);return; }
        if(upper is "BACK" or "ESC")
        {
            Back();
            if(terminalLines.Count==previousLines) PrintLine("ALREADY AT MAIN MENU");
            Dirty=true;
            return;
        }

        if(TitleScreen)
        {
            if(TitlePage==0) RunTitleCommand(upper);
            else RunJoinCommand(command,upper);
        }
        else if(View==MenuView.Pause) RunPauseCommand(upper);
        else if(View==MenuView.Categories) RunCategoryCommand(upper);
        else if(View==MenuView.Help) CommandFeedback="TYPE BACK TO RETURN";
        else if(View==MenuView.Spawn) RunSpawnCommand(upper);
        else RunSettingCommand(command,upper);
        if(terminalLines.Count==previousLines && CommandFeedback.Length>0)
            PrintLine(CommandFeedback);
        Dirty=true;
    }

    void RunTitleCommand(string command)
    {
        switch(command)
        {
            case "1": case "SOLO": case "PLAY": case "PLAY SOLO":
                TitleAction?.Invoke(0);break;
            case "2": case "ONLINE": case "JOIN": case "JOIN ONLINE":
                ShowJoinScreen();break;
            case "3": case "SETTINGS": case "OPTIONS":
                ShowSettingsFromTitle();break;
            case "4": case "QUIT": case "EXIT":
                TitleAction?.Invoke(3);break;
            default: CommandFeedback="UNKNOWN COMMAND  /  TYPE 1 TO 4";break;
        }
    }

    void RunJoinCommand(string command,string upper)
    {
        if(upper.StartsWith("ROOM ",StringComparison.Ordinal) ||
           upper.StartsWith("1 ",StringComparison.Ordinal))
        {
            int space=command.IndexOf(' ');
            JoinKey=command[(space+1)..].Trim();
            CommandFeedback=JoinKey.Length==0?"ROOM NAME REQUIRED":"ROOM SET  /  TYPE CONNECT";
            if(JoinKey.Length>0) PrintCurrentView();
            return;
        }
        switch(upper)
        {
            case "1": case "ROOM":
                CommandFeedback="TYPE ROOM <NAME>";break;
            case "2": case "CONNECT": case "JOIN":
                ConnectAction?.Invoke();
                CommandFeedback=NetworkStatus;
                break;
            case "3": ShowTitle(resetHistory:false);break;
            default:
                JoinKey=command.Trim();
                CommandFeedback="ROOM SET  /  TYPE CONNECT";
                PrintCurrentView();break;
        }
    }

    void RunPauseCommand(string command)
    {
        int row=command switch
        {
            "1" or "RESUME" or "CONTINUE"=>0,
            "2" or "PROFILE"=>1,
            "3" or "SETTINGS" or "OPTIONS"=>2,
            "4" or "DEVELOPER" or "DEBUG"=>3,
            "5" or "SPAWN"=>4,
            "6" or "HELP" or "CONTROLS"=>5,
            "7" or "LEAVE" or "LEAVE SESSION" or "MENU"=>6,
            "8" or "QUIT" or "EXIT"=>7,
            _=>-1
        };
        if(row<0) { CommandFeedback="UNKNOWN COMMAND  /  TYPE 1 TO 8";return; }
        Selected=row;
        Adjust(row,1);
    }

    void RunSpawnCommand(string command)
    {
        if(command=="HELP") { PrintCurrentView();return; }
        if(!CubeSpawner.TryParse(command,out CubeSpawnSpec spec,out string error))
        { CommandFeedback=error;return; }
        CommandFeedback=SpawnAction?.Invoke(spec)??"SPAWN UNAVAILABLE";
    }

    void RunCategoryCommand(string command)
    {
        int row=-1;
        if(int.TryParse(command,out int number)) row=number-1;
        else row=Array.FindIndex(Tabs,tab=>tab.Name==command ||
            tab.Name.Replace(" TUNING","",StringComparison.Ordinal)==command);
        if(row<0 || row>=Tabs.Length)
        { CommandFeedback=$"TYPE 1 TO {Tabs.Length} OR A PAGE NAME";return; }
        CategorySelected=row;
        SetPage(Tabs[row].Page);
    }

    void RunSettingCommand(string command,string upper)
    {
        switch(upper)
        {
            case "NEXT": case "PAGE NEXT": CyclePage(1);return;
            case "PREV": case "PREVIOUS": case "PAGE PREV": CyclePage(-1);return;
            case "RESET": ResetDefaults();CommandFeedback="DEFAULTS RESTORED";return;
            case "MORE":
                ScrollRow=Math.Min(Math.Max(0,Labels[Page].Length-VisibleRows),ScrollRow+VisibleRows);
                PrintCurrentView();return;
            case "LESS":
                ScrollRow=Math.Max(0,ScrollRow-VisibleRows);
                PrintCurrentView();return;
        }
        if(Page==6 && upper.StartsWith("NAME ",StringComparison.Ordinal))
        {
            SetPlayerName(command[5..]);
            return;
        }
        string argument=upper.StartsWith("SET ",StringComparison.Ordinal)?command[4..].Trim():command;
        string[] parts=argument.Split(' ',2,StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0 || !int.TryParse(parts[0],out int number) ||
           number<1 || number>Labels[Page].Length)
        {
            CommandFeedback="TYPE NUMBER  /  NEXT  /  BACK";
            return;
        }
        int row=number-1;
        Selected=row;
        EnsureVisible();
        if(parts.Length==1 || parts[1]=="+")
        {
            if(Page==6 && row==0)
            { CommandFeedback="TYPE NAME <TEXT>";return; }
            Adjust(row,1);
            CommandFeedback=$"{Labels[Page][row]}  {Value(row)}";
        }
        else if(parts[1]=="-")
        {
            Adjust(row,-1);
            CommandFeedback=$"{Labels[Page][row]}  {Value(row)}";
        }
        else if(Page==6 && row==0) SetPlayerName(parts[1]);
        else if(Page==5 && !MonsterControlsWritable) CommandFeedback="HOST ONLY";
        else if(TrySetValue(row,parts[1]))
            CommandFeedback=$"{Labels[Page][row]}  {Value(row)}";
        else CommandFeedback="VALUE NOT AVAILABLE";
    }

    void SetPlayerName(string name)
    {
        string cleaned=new(name.Trim().ToUpperInvariant()
            .Where(c=>char.IsAsciiLetterOrDigit(c)||c is ' ' or '-').Take(12).ToArray());
        if(cleaned.Length==0) { CommandFeedback="NAME REQUIRED";return; }
        PlayerName=cleaned;
        CommandFeedback="PLAYER NAME  "+PlayerName;
    }

    bool TrySetValue(int row,string wanted)
    {
        static string Clean(string value)=>value.Trim().ToUpperInvariant()
            .Replace(" FPS","",StringComparison.Ordinal)
            .Replace(" SPP","",StringComparison.Ordinal)
            .Replace(',','.');
        string target=Clean(wanted);
        if(Page==6 && row==1)
        {
            int colorIndex=Array.FindIndex(CubeColors,color=>color.Name==target);
            if(colorIndex<0) return false;
            PlayerColorIndex=colorIndex;
            PlayerRed=CubeColors[colorIndex].Color.X;
            PlayerGreen=CubeColors[colorIndex].Color.Y;
            PlayerBlue=CubeColors[colorIndex].Color.Z;
            Dirty=true;
            return true;
        }
        bool Matches(string value)
        {
            string current=Clean(value);
            if(current==target) return true;
            return double.TryParse(current,NumberStyles.Float,CultureInfo.InvariantCulture,out double a) &&
                double.TryParse(target,NumberStyles.Float,CultureInfo.InvariantCulture,out double b) &&
                Math.Abs(a-b)<0.005;
        }
        if(Matches(Value(row))) return true;
        string original=Value(row);
        HashSet<string> seen=[original];
        for(int attempt=0;attempt<1000;attempt++)
        {
            string before=Value(row);
            Adjust(row,1,false);
            string after=Value(row);
            if(Matches(after)) return true;
            if(!seen.Add(after)) break;
        }
        for(int attempt=0;attempt<1000 && Value(row)!=original;attempt++)
        {
            string before=Value(row);
            Adjust(row,-1,false);
            if(Value(row)==before) break;
        }
        return false;
    }
}
