using System.Globalization;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    bool commandError;
    void CommandFailed(string reason)
    {
        CommandFeedback=reason;
        commandError=true;
    }

    internal void ClearCommand()
    {
        CommandLine="";
        CommandFeedback="";
        commandError=false;
        Dirty=true;
    }

    internal void TypeCommandCharacter(char c)
    {
        if(!Open || char.IsControl(c) || CommandLine.Length>=128) return;
        CommandLine+=char.ToUpperInvariant(c);
        InputSound?.Invoke();
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
        InputSound?.Invoke();
        CommandFeedback="";
        Dirty=true;
    }

    internal void SubmitCommand()
    {
        if(!Open) return;
        string command=CommandLine.Trim();
        CommandLine="";
        if(command.Length==0) { Dirty=true;return; }
        InputSound?.Invoke();
        string upper=command.ToUpperInvariant();
        CommandFeedback="";
        commandError=false;
        if(upper is "CLS" or "CLEAR")
        {
            ResetTerminal();
            PrintCurrentView();
            return;
        }
        if(!TitleScreen && View==MenuView.Network && upper is not ("BACK" or "ESC"))
        {
            RunChatCommand(command);
            return;
        }
        FlushTerminal();
        bool scrolling=upper is "SCROLL UP" or "PAGE UP" or "SCROLL DOWN" or "PAGE DOWN" or "TOP" or "BOTTOM";
        if(!scrolling) ScrollTerminal(-1000);
        PrintLine(MenuPath+"> "+upper,inputStart:MenuPath.Length+2);
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
            if(terminalLines.Count==previousLines) PrintLine("ERROR: ALREADY AT MAIN MENU",error:true);
            Dirty=true;
            return;
        }

        if(TitleScreen)
        {
            if(TitlePage==0) RunTitleCommand(upper);
            else RunJoinCommand(command,upper);
        }
        else if(!SettingsFromTitle && (upper=="SPAWN" || upper.StartsWith("SPAWN ",StringComparison.Ordinal)))
            RunSpawnCommand(upper.Length>6?upper[6..]:"");
        else if(View==MenuView.Pause) RunPauseCommand(upper);
        else if(View==MenuView.Categories) RunCategoryCommand(upper);
        else if(View==MenuView.Help) CommandFailed("COMMAND NOT RECOGNIZED");
        else RunSettingCommand(command,upper);
        if(terminalLines.Count==previousLines && Open)
        {
            if(CommandFeedback.Length==0) CommandFailed("COMMAND NOT RECOGNIZED");
            PrintLine((commandError?"ERROR: ":"")+CommandFeedback,error:commandError);
        }
        if(View==MenuView.Settings) RefreshSettingLines();
        Dirty=true;
    }

    void RunTitleCommand(string command)
    {
        switch(command)
        {
            case "SOLO": case "PLAY": case "PLAY SOLO":
                TitleAction?.Invoke(0);break;
            case "ONLINE": case "JOIN": case "JOIN ONLINE":
                ShowJoinScreen();break;
            case "SETTINGS": case "OPTIONS":
                ShowSettingsFromTitle();break;
            case "QUIT": case "EXIT":
                TitleAction?.Invoke(3);break;
            default: CommandFailed("COMMAND NOT RECOGNIZED");break;
        }
    }

    void RunJoinCommand(string command,string upper)
    {
        if(upper.StartsWith("ROOM ",StringComparison.Ordinal))
        {
            int space=command.IndexOf(' ');
            JoinKey=command[(space+1)..].Trim();
            if(JoinKey.Length==0) CommandFailed("ROOM NAME REQUIRED");
            else CommandFeedback="ROOM SET  /  TYPE CONNECT";
            if(JoinKey.Length>0) PrintCurrentView();
            return;
        }
        switch(upper)
        {
            case "ROOM":
                CommandFailed("ROOM NAME REQUIRED");break;
            case "CONNECT": case "JOIN":
                ConnectAction?.Invoke();
                CommandFeedback=NetworkStatus;
                break;
            case "BACK": ShowTitle(resetHistory:false);break;
            default:
                if(upper.StartsWith("CONNECT ",StringComparison.Ordinal))
                {
                    JoinKey=command[8..].Trim();
                    if(JoinKey.Length==0) { CommandFailed("ROOM NAME REQUIRED");break; }
                    PrintCurrentView();
                    ConnectAction?.Invoke();
                    CommandFeedback=NetworkStatus;
                }
                else CommandFailed("COMMAND NOT RECOGNIZED");
                break;
        }
    }

    void RunPauseCommand(string command)
    {
        int row=command switch
        {
            "RESUME" or "CONTINUE"=>0,
            "PROFILE"=>1,
            "SETTINGS" or "OPTIONS"=>2,
            "DEVELOPER" or "DEBUG"=>3,
            "NETWORK" when NetworkConnected=>7,
            "HELP" or "CONTROLS"=>4,
            "LEAVE" or "LEAVE SESSION" or "MENU"=>5,
            "QUIT" or "EXIT"=>6,
            _=>-1
        };
        if(row<0) { CommandFailed("COMMAND NOT RECOGNIZED");return; }
        Selected=row;
        Page=9;
        if(row==7) OpenNetwork();
        else Adjust(row,1);
    }

    void RunChatCommand(string command)
    {
        if(command.Equals("PLAYERS",StringComparison.OrdinalIgnoreCase))
        { OpenNetworkPlayers();return; }
        if(networkPlayersOpen)
        { CommandFailed("COMMAND NOT RECOGNIZED");PrintLine("ERROR: "+CommandFeedback,error:true);return; }
        string text=command.StartsWith("CHAT ",StringComparison.OrdinalIgnoreCase)
            ?command[5..]:command;
        text=CleanChatText(text);
        if(text.Length==0) CommandFailed("MESSAGE REQUIRED");
        else if(text.Length>100) CommandFailed("MESSAGE TOO LONG");
        else if(ChatAction?.Invoke(text)!=true) CommandFailed("CHAT UNAVAILABLE");
        if(commandError) PrintLine("ERROR: "+CommandFeedback,error:true);
        Dirty=true;
    }

    void RunSpawnCommand(string command)
    {
        if(command.Length==0) { CommandFailed("SPAWN REQUIRES TYPE SIZE COLOR AMOUNT");return; }
        if(!CubeSpawner.TryParse(command,out CubeSpawnSpec spec,out string error))
        { CommandFailed(error);return; }
        string result=SpawnAction?.Invoke(spec)??"SPAWN UNAVAILABLE";
        if(result=="SPAWN REQUEST SENT" || result=="SPAWNED" ||
           result.StartsWith("SPAWNED ",StringComparison.Ordinal)) CommandFeedback=result;
        else CommandFailed(result);
    }

    void RunCategoryCommand(string command)
    {
        int row=Array.FindIndex(Tabs,tab=>tab.Name==command ||
            tab.Name.Replace(" TUNING","",StringComparison.Ordinal)==command);
        if(row<0 || row>=Tabs.Length)
        { CommandFailed("COMMAND NOT RECOGNIZED");return; }
        CategorySelected=row;
        SetPage(Tabs[row].Page);
    }

    void RunSettingCommand(string command,string upper)
    {
        switch(upper)
        {
            case "RESET": ResetDefaults();CommandFeedback="DEFAULTS RESTORED";return;
        }
        if(Page==6 && upper.StartsWith("NAME ",StringComparison.Ordinal))
        {
            SetPlayerName(command[5..]);
            return;
        }
        string argument=upper.StartsWith("SET ",StringComparison.Ordinal)?command[4..].Trim():command;
        int row=Array.FindIndex(Labels[Page],label=>argument.Equals(label,StringComparison.OrdinalIgnoreCase));
        string value="";
        if(row<0)
        {
            row=Array.FindIndex(Labels[Page],label=>argument.StartsWith(label+" ",StringComparison.OrdinalIgnoreCase));
            if(row>=0) value=argument[(Labels[Page][row].Length+1)..].Trim();
        }
        if(row<0)
        {
            CommandFailed("COMMAND NOT RECOGNIZED");
            return;
        }
        Selected=row;
        if(value.Length==0 || value=="+")
        {
            if(Page==6 && row==0)
            { CommandFailed("NAME REQUIRED");return; }
            Adjust(row,1);
            CommandFeedback=$"{Labels[Page][row]}  {Value(row)}";
        }
        else if(value=="-")
        {
            Adjust(row,-1);
            CommandFeedback=$"{Labels[Page][row]}  {Value(row)}";
        }
        else if(Page==6 && row==0) SetPlayerName(value);
        else if(Page==5 && !MonsterControlsWritable) CommandFailed("HOST ONLY");
        else if(TrySetValue(row,value))
            CommandFeedback=$"{Labels[Page][row]}  {Value(row)}";
        else CommandFailed("VALUE NOT AVAILABLE");
    }

    void SetPlayerName(string name)
    {
        string cleaned=new(name.Trim().ToUpperInvariant()
            .Where(c=>char.IsAsciiLetterOrDigit(c)||c is ' ' or '-').Take(12).ToArray());
        if(cleaned.Length==0) { CommandFailed("NAME REQUIRED");return; }
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
        for(int attempt=0;attempt<1000;attempt++)
        {
            string before=Value(row);
            Adjust(row,-1,false);
            string after=Value(row);
            if(Matches(after)) return true;
            if(after==before) break;
        }
        for(int attempt=0;attempt<1000 && Value(row)!=original;attempt++)
        {
            string before=Value(row);
            Adjust(row,1,false);
            if(Value(row)==before) break;
        }
        return false;
    }
}
