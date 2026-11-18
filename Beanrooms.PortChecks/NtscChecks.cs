using System.Diagnostics;
using Beanrooms;

internal static class NtscChecks
{
    internal static void Run()
    {
        static void Check(bool value,string message) { if(!value) throw new Exception(message); }
        var descriptors=NtscSettings.Descriptors;
        Check(descriptors.Length==60,"Upstream NTSC schema lost settings.");
        var exposed=NtscSettings.Pages.SelectMany(p=>p.Rows).ToArray();
        Check(exposed.Length==descriptors.Length && exposed.Select(d=>d.Key).Distinct().Count()==descriptors.Length,
            "NTSC menu must expose every upstream control exactly once.");
        var menu=new DebugMenu();
        void Send(string command) { menu.PasteCommand(command);menu.SubmitCommand(); }
        menu.ShowTitle();menu.ShowSettingsFromTitle();Send("NTSC");
        Check(menu.Page==13,"CMD NTSC category did not open.");
        Send("ENABLED OFF");Check(!menu.Ntsc.Enabled,"NTSC enable command failed.");
        Send("STRENGTH 0.42");Check(Math.Abs(menu.Ntsc.Strength-.42f)<.001f,"NTSC strength command failed.");
        Send("SIGNAL");Check(menu.Page==14,"NTSC signal page did not open.");
        Send("RANDOM SEED -2147483648");
        Check((int)menu.Ntsc.Get("random_seed")==int.MinValue,"NTSC integer precision/range was lost.");
        Send("USE FIELD BOTH");Check((int)menu.Ntsc.Get("use_field")==3,"NTSC named enumeration command failed.");
        Send("RANDOM SEED 999999999999");
        Check((int)menu.Ntsc.Get("random_seed")==int.MinValue,"Invalid NTSC numeric input changed its setting.");
        Send("BACK");Check(menu.Page==13,"NTSC back did not return to its hub.");
        Send("VHS TAPE");Send("VHS EMULATION OFF");
        Send("TAPE SPEED SP");Check(menu.CommandFeedback=="GROUP DISABLED","Disabled NTSC group accepted edits.");
        Send("RESET");Check((bool)menu.Ntsc.Get("vhs_settings"),"NTSC group reset failed.");
        Send("TAPE SPEED SP");Check((int)menu.Ntsc.Get("vhs_tape_speed")==1,"NTSC tape speed did not change.");
        Send("BACK");Send("RESET");Check(menu.Ntsc.Enabled&&menu.Ntsc.Strength==.65f,"NTSC hub reset failed.");
        for(int page=0;page<NtscSettings.Pages.Length;page++) {
            menu.SetPage(page+14);menu.FlushTerminal();
            Check(NtscSettings.Pages[page].Rows.Select(NtscSettings.Label).Distinct().Count()==NtscSettings.Pages[page].Rows.Length,
                "NTSC abbreviated labels must remain distinct within each page.");
            using var image=menu.Draw();Check(image.Width==320&&image.Height==240,"NTSC terminal draw failed.");
        }
        using var processor=new NtscProcessor();
        // Every upstream setting must survive adjustment and native JSON parsing.
        foreach(var d in descriptors) {
            var settings=new NtscSettings();
            var ancestor=d.Parent;
            while(ancestor!=null) {
                var parent=descriptors.First(x=>x.Key==ancestor);
                if(!(bool)settings.Get(ancestor)) settings.Adjust(parent,1);
                ancestor=parent.Parent;
            }
            settings.Adjust(d,1);
            var pixels=Pattern(64,48);
            processor.Process(pixels,64,48,42,settings);
            Check(pixels.All(float.IsFinite),$"NTSC setting {d.Key} produced non-finite output.");
        }
        var defaults=new NtscSettings();
        var source=Pattern(320,240);
        var first=(float[])source.Clone();processor.Process(first,320,240,42,defaults);
        var repeated=(float[])source.Clone();processor.Process(repeated,320,240,42,defaults);
        Check(first.SequenceEqual(repeated),"NTSC is not deterministic for identical seed/field.");
        Check(!first.SequenceEqual(source),"NTSC effect did not change the image.");
        var next=(float[])source.Clone();processor.Process(next,320,240,43,defaults);
        Check(!first.SequenceEqual(next),"NTSC noise did not animate between fields.");
        defaults.Strength=0;var bypass=(float[])source.Clone();processor.Process(bypass,320,240,42,defaults);
        Check(bypass.SequenceEqual(source),"Zero NTSC strength did not preserve the source.");
        defaults.Strength=1;
        // The source top is bright and its bottom is dark. GL uses bottom-up data;
        // the wrapper must flip at both conversion boundaries, not the final image.
        var orientation=new float[320*240*4];
        for(int y=0;y<240;y++) for(int x=0;x<320;x++) for(int c=0;c<4;c++)
            orientation[(y*320+x)*4+c]=c==3?1:y>120?0.9f:0.1f;
        processor.Process(orientation,320,240,42,defaults);
        Check(orientation[(200*320+160)*4]>orientation[(40*320+160)*4]+0.3f,"NTSC image was flipped vertically.");
        foreach(var (w,h) in new[] {(320,240),(480,360),(640,480)}) {
            var pixels=Pattern(w,h);processor.Process(pixels,w,h,42,defaults);
            var clock=Stopwatch.StartNew();
            for(uint field=43;field<48;field++) { pixels=Pattern(w,h);processor.Process(pixels,w,h,field,defaults); }
            clock.Stop();
            Check(pixels.All(p=>float.IsFinite(p)&&p>=0&&p<=1),"NTSC resize produced invalid colors.");
            Console.WriteLine($"NTSC native {w}x{h}: {clock.Elapsed.TotalMilliseconds/5:0.0} ms/frame (includes pattern allocation).");
        }
        Console.WriteLine($"NTSC checks passed: {descriptors.Length} upstream controls across {NtscSettings.Pages.Length} pages.");
    }
    static float[] Pattern(int w,int h)
    {
        var pixels=new float[w*h*4];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) {
            int p=(y*w+x)*4;
            pixels[p]=x/(float)w;pixels[p+1]=y/(float)h;pixels[p+2]=(x/8%2)*0.8f;pixels[p+3]=1;
        }
        return pixels;
    }
}

