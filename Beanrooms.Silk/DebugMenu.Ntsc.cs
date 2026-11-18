namespace Beanrooms;

internal sealed partial class DebugMenu
{
    static readonly string[][] Labels = BaseLabels
        .Concat(new[] { new[] { "ENABLED", "STRENGTH" }.Concat(NtscSettings.Pages.Select(p=>p.Title)).ToArray() })
        .Concat(NtscSettings.Pages.Select(p=>p.Rows.Select(NtscSettings.Label).ToArray())).ToArray();
    NtscSettings.Descriptor NtscRow(int row)=>NtscSettings.Pages[Page-14].Rows[row];
    string NtscValue(int row)=>Page==13?row switch {
        0=>Ntsc.Enabled?"ON":"OFF",1=>$"{Ntsc.Strength:0.00}",_=>"OPEN"
    }:Ntsc.Value(NtscRow(row));
    void NtscSetFraction(int row,float value)
    {
        if(Page==13) { if(row==1) Ntsc.Strength=Math.Clamp(value,0f,1f); }
        else Ntsc.SetFraction(NtscRow(row),value);
    }
    bool NtscTrySet(int row,string wanted)
    {
        if(Page>=14) return Ntsc.TrySet(NtscRow(row),wanted);
        if(row==0 && wanted is "ON" or "OFF") { Ntsc.Enabled=wanted=="ON";return true; }
        if(row==1 && float.TryParse(wanted,System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,out float value) && float.IsFinite(value) && value>=0f && value<=1f)
        { Ntsc.Strength=value;return true; }
        return false;
    }
    void NtscAdjust(int row,int direction)
    {
        if(Page==13) {
            if(row==0) Ntsc.Enabled=!Ntsc.Enabled;
            else if(row==1) Ntsc.Strength=Math.Clamp(Ntsc.Strength+direction*0.01f,0f,1f);
            else SetPage(14+row-2);
        }
        else Ntsc.Adjust(NtscRow(row),direction);
    }
}
