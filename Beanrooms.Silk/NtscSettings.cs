using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Beanrooms;

// Schema, defaults, ranges, enumeration values, and groups come from the pinned
// upstream library. The menu and native processor share the same setting keys.
internal sealed class NtscSettings
{
    internal sealed record Option(string Label,int Value);
    internal sealed record Descriptor(string Key,string Label,string Description,string? Parent,
        string Kind,double Min,double Max,bool Log,JsonElement Default,Option[] Options);
    internal sealed record SettingsPage(string Title,Descriptor[] Rows);
    internal static readonly Descriptor[] Descriptors=ReadSchema();
    internal static readonly SettingsPage[] Pages=BuildPages();
    readonly Dictionary<string,object> values=new();
    internal bool Enabled=true;
    internal float Strength=0.65f;
    internal int Revision { get; private set; }
    internal NtscSettings() => Reset();
    static Descriptor[] ReadSchema()
    {
        string json=Marshal.PtrToStringUTF8(NtscNative.Schema()) ?? throw new InvalidOperationException("NTSC schema unavailable.");
        using var document=JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(d=>new Descriptor(
            d.GetProperty("key").GetString()!,d.GetProperty("label").GetString()!,
            d.GetProperty("description").ValueKind==JsonValueKind.String?d.GetProperty("description").GetString()!:"",
            d.GetProperty("parent").GetString(),d.GetProperty("kind").GetString()!,
            d.TryGetProperty("min",out var min)?min.GetDouble():0,
            d.TryGetProperty("max",out var max)?max.GetDouble():1,
            d.TryGetProperty("log",out var log)&&log.GetBoolean(),d.GetProperty("default").Clone(),
            d.TryGetProperty("options",out var options)?options.EnumerateArray().Select(o=>new Option(o.GetProperty("label").GetString()!,o.GetProperty("value").GetInt32())).ToArray():[])).ToArray();
    }
    static SettingsPage[] BuildPages()
    {
        var pages=new List<SettingsPage>();
        var color=new HashSet<string>(["chroma_lowpass_in","chroma_demodulation","chroma_phase_noise_intensity","chroma_phase_error","chroma_delay_horizontal","chroma_delay_vertical","vhs_chroma_vert_blend","chroma_lowpass_out"]);
        var roots=Descriptors.Where(d=>d.Parent==null).ToArray();
        bool Group(Descriptor d)=>Descriptors.Any(c=>c.Parent==d.Key);
        pages.Add(new("SIGNAL",roots.Where(d=>!Group(d)&&!color.Contains(d.Key)).ToArray()));
        pages.Add(new("COLOR",roots.Where(d=>!Group(d)&&color.Contains(d.Key)).ToArray()));
        foreach(var root in roots.Where(Group))
        {
            bool InGroup(Descriptor d) => d.Key==root.Key || d.Parent!=null && InGroup(Descriptors.First(x=>x.Key==d.Parent));
            string title=root.Key switch {
                "composite_noise"=>"COMPOSITE NOISE","luma_noise"=>"LUMA NOISE","chroma_noise"=>"CHROMA NOISE",
                "head_switching"=>"HEAD SWITCHING","tracking_noise"=>"TRACKING NOISE","vhs_settings"=>"VHS TAPE",
                "scale_settings"=>"SCALING",_=>root.Label.ToUpperInvariant()
            };
            pages.Add(new(title,Descriptors.Where(InGroup).ToArray()));
        }
        return pages.ToArray();
    }
    internal object Get(string key)=>values[key];
    internal void Reset(int page=-1)
    {
        foreach(var d in page<0?Descriptors:Pages[page].Rows)
            values[d.Key]=d.Kind=="bool"?d.Default.GetBoolean():d.Kind is "int" or "enum"?(object)d.Default.GetInt32():d.Default.GetDouble();
        if(page<0) { Enabled=true;Strength=0.65f; }
        Revision++;
    }
    internal bool Locked(Descriptor d) => d.Parent!=null &&
        (!(bool)values[d.Parent] || Locked(Descriptors.First(x=>x.Key==d.Parent)));
    internal double Number(Descriptor d)=>Convert.ToDouble(values[d.Key],CultureInfo.InvariantCulture);
    internal float Fraction(Descriptor d)
    {
        double t=(Number(d)-d.Min)/(d.Max-d.Min);
        // A power slider preserves zero and gives small noise/chroma-loss values usable precision.
        return (float)(d.Log?Math.Pow(Math.Clamp(t,0,1),0.25):t);
    }
    internal void SetFraction(Descriptor d,float fraction)
    {
        if(Locked(d)) return;
        double t=Math.Clamp(fraction,0f,1f);
        double value=d.Min+(d.Max-d.Min)*(d.Log?Math.Pow(t,4):t);
        values[d.Key]=d.Kind=="int"?(object)(int)Math.Clamp(Math.Round(value),d.Min,d.Max):value;
        Revision++;
    }
    internal void Adjust(Descriptor d,int direction)
    {
        if(Locked(d)) return;
        if(d.Kind=="bool") values[d.Key]=!(bool)values[d.Key];
        else if(d.Kind=="enum") {
            int index=Array.FindIndex(d.Options,o=>o.Value==(int)values[d.Key]);
            values[d.Key]=d.Options[(index+direction+d.Options.Length)%d.Options.Length].Value;
        }
        else if(d.Kind=="int") values[d.Key]=(int)Math.Clamp(Number(d)+direction,d.Min,d.Max);
        else { SetFraction(d,Math.Clamp(Fraction(d)+direction*0.005f,0f,1f));return; }
        Revision++;
    }
    internal bool TrySet(Descriptor d,string text)
    {
        if(Locked(d)) return false;
        if(d.Kind=="bool") {
            if(text is not ("ON" or "OFF" or "TRUE" or "FALSE")) return false;
            values[d.Key]=text is "ON" or "TRUE";
        }
        else if(d.Kind=="enum") {
            var option=d.Options.FirstOrDefault(o=>o.Label.Equals(text,StringComparison.OrdinalIgnoreCase) ||
                ShortOption(o.Label)==text || o.Value.ToString(CultureInfo.InvariantCulture)==text);
            if(option==null) return false;
            values[d.Key]=option.Value;
        }
        else {
            if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value) ||
               !double.IsFinite(value) || value<d.Min || value>d.Max || d.Kind=="int" && value!=Math.Truncate(value)) return false;
            values[d.Key]=d.Kind=="int"?(object)(int)value:value;
        }
        Revision++;return true;
    }
    internal string Value(Descriptor d)=> d.Kind switch {
        "bool"=>(bool)values[d.Key]?"ON":"OFF",
        "enum"=>ShortOption(d.Options.First(o=>o.Value==(int)values[d.Key]).Label),
        "int"=>((int)values[d.Key]).ToString(CultureInfo.InvariantCulture),
        _=>Number(d).ToString("0.#####",CultureInfo.InvariantCulture)
    };
    static string ShortOption(string label)=>label switch {
        "Interleaved (upper first)"=>"INT UPPER","Interleaved (lower first)"=>"INT LOWER",
        "Constant K (blurry)"=>"CONSTANT K","Butterworth (sharper)"=>"BUTTERWORTH",
        "SP (Standard Play)"=>"SP","LP (Long Play)"=>"LP","EP (Extended Play)"=>"EP",
        "1-line comb"=>"1-LINE COMB","2-line comb"=>"2-LINE COMB",
        _=>label.Length<=12?label.ToUpperInvariant():label[..12].ToUpperInvariant()
    };
    internal static string Label(Descriptor d)
    {
        string label=(d.Key switch {
            "vhs_tape_speed"=>"TAPE SPEED",
            "chroma_delay_horizontal"=>"CHROMA DELAY HORIZ",
            "chroma_delay_vertical"=>"CHROMA DELAY VERT",
            "vhs_chroma_vert_blend"=>"BLEND CHROMA VERT",
            "chroma_demodulation"=>"DEMODULATION FILTER",
            "vhs_sharpen"=>"SHARPEN AMOUNT",
            "vhs_sharpen_frequency"=>"SHARPEN FREQUENCY",
            "vhs_edge_wave"=>"EDGE WAVE AMOUNT",
            "vhs_edge_wave_speed"=>"EDGE WAVE SPEED",
            "vhs_edge_wave_frequency"=>"EDGE WAVE FREQUENCY",
            "vhs_edge_wave_detail"=>"EDGE WAVE DETAIL",
            _=>d.Label.ToUpperInvariant()
        }).Replace("COMPOSITE SIGNAL ","COMPOSITE ")
            .Replace("HORIZONTAL","HORIZ").Replace("VERTICAL","VERT").Replace("CHROMA DEMODULATION","DEMODULATION")
            .Replace("LOW-PASS","LOWPASS").Replace("INTENSITY","AMOUNT").Replace("SHARPENING","SHARPEN")
            .Replace("SCALE WITH VIDEO SIZE","SCALE WITH SIZE");
        return label.Length<=18?label:label[..18];
    }
    internal string ToJson()
    {
        var serialized=new Dictionary<string,object>(values) { ["version"]=1 };
        return JsonSerializer.Serialize(serialized);
    }
}

internal static class NtscNative
{
    const string Library="beanrooms_ntsc";
    [DllImport(Library,EntryPoint="bean_ntsc_schema",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Schema();
    [DllImport(Library,EntryPoint="bean_ntsc_create",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Create();
    [DllImport(Library,EntryPoint="bean_ntsc_destroy",CallingConvention=CallingConvention.Cdecl)] internal static extern void Destroy(IntPtr effect);
    [DllImport(Library,EntryPoint="bean_ntsc_settings",CallingConvention=CallingConvention.Cdecl)] internal static extern int Settings(NtscProcessor effect,[MarshalAs(UnmanagedType.LPUTF8Str)]string json);
    [DllImport(Library,EntryPoint="bean_ntsc_process",CallingConvention=CallingConvention.Cdecl)] internal static extern int Process(NtscProcessor effect,[In,Out]float[] pixels,int width,int height,uint frame,float strength);
}
internal sealed class NtscProcessor : SafeHandleZeroOrMinusOneIsInvalid
{
    int appliedRevision=-1;
    NtscSettings? appliedSettings;
    internal NtscProcessor():base(true) { SetHandle(NtscNative.Create());if(IsInvalid) throw new InvalidOperationException("Unable to initialize ntsc-rs."); }
    protected override bool ReleaseHandle() { NtscNative.Destroy(handle);return true; }
    internal void Process(float[] pixels,int width,int height,uint field,NtscSettings settings)
    {
        if(!ReferenceEquals(appliedSettings,settings) || appliedRevision!=settings.Revision) {
            if(NtscNative.Settings(this,settings.ToJson())!=0) throw new InvalidOperationException("ntsc-rs rejected effect settings.");
            appliedRevision=settings.Revision;appliedSettings=settings;
        }
        if(NtscNative.Process(this,pixels,width,height,field,settings.Strength)!=0)
            throw new InvalidOperationException("ntsc-rs frame processing failed.");
    }
}
