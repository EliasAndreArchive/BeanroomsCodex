using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    readonly record struct NetworkMember(int Id,string Name,Color Ink);
    readonly record struct ChatEntry(int Sender,string Name,Color Ink,string Text,bool Resolved);
    readonly record struct ChatRow(string Text,Color? NameInk,int NameLength);
    NetworkMember[] networkMembers=[];
    readonly List<ChatEntry> networkChat=[];
    int networkChatOffset;
    bool networkPlayersOpen;
    int displayedNetworkHost,displayedNetworkPlayers;
    const int ChatWindow=10;

    internal void ResetNetwork()
    {
        NetworkRoomCode="";
        networkMembers=[];
        networkChat.Clear();
        networkChatOffset=0;
        networkPlayersOpen=false;
        displayedNetworkHost=displayedNetworkPlayers=0;
    }

    internal void UpdateNetworkRoom(string code,PlayerState[] players,int hostId,int localId)
    {
        NetworkMember[] members=players.OrderBy(player=>player.Id)
            .Select(player=>new NetworkMember(player.Id,
                CleanPlayerName(player.Name,player.Id),PlayerInk(player.R,player.G,player.B))).ToArray();
        if(members.All(player=>player.Id!=localId) && localId>0)
            members=[..members,new NetworkMember(localId,CleanPlayerName(PlayerName,localId),
                PlayerInk(PlayerRed,PlayerGreen,PlayerBlue))];
        if(NetworkRoomCode==code && displayedNetworkHost==hostId &&
            displayedNetworkPlayers==NetworkPlayers && networkMembers.SequenceEqual(members)) return;
        NetworkRoomCode=code;
        NetworkHost=hostId;
        displayedNetworkHost=hostId;
        displayedNetworkPlayers=NetworkPlayers;
        networkMembers=members;
        for(int i=0;i<networkChat.Count;i++)
        {
            ChatEntry entry=networkChat[i];
            if(entry.Resolved) continue;
            NetworkMember sender=members.FirstOrDefault(player=>player.Id==entry.Sender);
            if(sender.Id!=0) networkChat[i]=entry with { Name=sender.Name,Ink=sender.Ink,Resolved=true };
        }
        RefreshNetworkView();
    }

    internal void ReceiveNetworkChat(int sender,string text)
    {
        string cleaned=CleanChatText(text);
        if(cleaned.Length is 0 or >100) return;
        int previousRows=ChatRows().Count;
        NetworkMember member=networkMembers.FirstOrDefault(player=>player.Id==sender);
        string name=member.Id==0?$"BEAN{sender}":member.Name;
        Color ink=member.Id==0?Color.FromArgb(210,210,210):member.Ink;
        networkChat.Add(new ChatEntry(sender,name,ink,cleaned,member.Id!=0));
        if(networkChat.Count>100) networkChat.RemoveAt(0);
        int rows=ChatRows().Count;
        if(networkChatOffset>0)
            networkChatOffset=Math.Min(networkChatOffset+Math.Max(0,rows-previousRows),
                Math.Max(0,rows-ChatWindow));
        RefreshNetworkView();
    }

    internal void ScrollNetworkChat(int direction)
    {
        if(networkPlayersOpen) return;
        int offset=Math.Clamp(networkChatOffset+direction,0,Math.Max(0,ChatRows().Count-ChatWindow));
        if(offset==networkChatOffset) return;
        networkChatOffset=offset;
        RefreshNetworkView();
    }

    void PrintNetworkContents()
    {
        if(networkPlayersOpen)
        {
            PrintLine("ROOM      "+NetworkRoomCode);
            foreach(NetworkMember member in networkMembers.Take(4))
            {
                string prefix=$"P{member.Id}  ";
                PrintLine(prefix+member.Name+(member.Id==NetworkHost?"  HOST":"")+
                    (member.Id==NetworkId?"  YOU":""));
                terminalLines[^1].ColorSpans=[(prefix.Length,member.Name.Length,member.Ink)];
            }
            PrintLine("BACK",action:"BACK");
            return;
        }
        PrintLine("ROOM      "+NetworkRoomCode);
        NetworkMember host=networkMembers.FirstOrDefault(player=>player.Id==NetworkHost);
        string hostName=host.Id==0?$"BEAN{NetworkHost}":host.Name;
        PrintLine(("HOST      "+hostName).PadRight(29)+$"PLAYERS {NetworkPlayers}/4");
        if(host.Id!=0)
            terminalLines[^1].ColorSpans=[(10,host.Name.Length,host.Ink)];
        PrintLine("CHAT");
        List<ChatRow> rows=ChatRows();
        int end=Math.Max(0,rows.Count-networkChatOffset);
        for(int i=Math.Max(0,end-ChatWindow);i<end;i++)
        {
            ChatRow row=rows[i];
            PrintLine(row.Text);
            if(row.NameInk is Color ink)
                terminalLines[^1].ColorSpans=[(0,row.NameLength,ink)];
        }
        PrintLine("PLAYERS",action:"PLAYERS");
        PrintLine("BACK",action:"BACK");
    }

    void OpenNetworkPlayers()
    {
        networkPlayersOpen=true;
        PrintCurrentView();
    }

    bool BackFromNetworkPlayers()
    {
        if(!networkPlayersOpen) return false;
        networkPlayersOpen=false;
        PrintCurrentView();
        return true;
    }

    void RefreshNetworkView()
    {
        if(!Open || TitleScreen || View!=MenuView.Network || currentMenuStart>=terminalLines.Count) return;
        var shown=new Dictionary<string,Queue<int>>();
        foreach(TerminalLine line in terminalLines.Skip(currentMenuStart).Take(currentMenuEnd-currentMenuStart))
        {
            if(!shown.TryGetValue(line.Text,out Queue<int>? visible)) shown[line.Text]=visible=new Queue<int>();
            visible.Enqueue(line.Visible);
        }
        terminalLines.RemoveRange(currentMenuStart,terminalLines.Count-currentMenuStart);
        PrintLine(MenuPath+"> DIR");
        PrintNetworkContents();
        currentMenuEnd=terminalLines.Count;
        foreach(TerminalLine line in terminalLines.Skip(currentMenuStart).Take(currentMenuEnd-currentMenuStart))
            if(shown.TryGetValue(line.Text,out Queue<int>? visible) && visible.Count>0)
                line.Visible=visible.Dequeue();
        PrintLine();
        terminalScroll=0;
        Dirty=true;
    }

    static string CleanPlayerName(string name,int id)
    {
        string cleaned=new string((name??"").ToUpperInvariant()
            .Where(c=>Glyphs.ContainsKey(c)).Take(12).ToArray()).Trim();
        return cleaned.Length==0?$"BEAN{id}":cleaned;
    }

    static Color PlayerInk(float red,float green,float blue)
        => Color.FromArgb((int)(Math.Clamp(red,0f,1f)*255),
            (int)(Math.Clamp(green,0f,1f)*255),(int)(Math.Clamp(blue,0f,1f)*255));

    static string CleanChatText(string text)
        => new string((text??"").ToUpperInvariant().Where(c=>Glyphs.ContainsKey(c)).ToArray()).Trim();

    List<ChatRow> ChatRows()
    {
        var rows=new List<ChatRow>();
        foreach(ChatEntry entry in networkChat)
        {
            string remaining=entry.Text;
            string prefix=entry.Name+"> ";
            bool first=true;
            while(remaining.Length>0)
            {
                int capacity=49-prefix.Length;
                int length=Math.Min(remaining.Length,capacity);
                if(length<remaining.Length)
                {
                    int space=remaining.LastIndexOf(' ',length-1,length);
                    if(space>=capacity/2) length=space;
                }
                string chunk=remaining[..length].TrimEnd();
                rows.Add(new ChatRow(prefix+chunk,first?entry.Ink:null,first?entry.Name.Length:0));
                remaining=remaining[length..].TrimStart();
                prefix="  ";
                first=false;
            }
        }
        return rows;
    }
}
