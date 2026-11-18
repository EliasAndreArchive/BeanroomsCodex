using System.Runtime.InteropServices;
using Beanrooms;
using Silk.NET.Maths;
using Silk.NET.Windowing;

internal static class NtscGpuChecks
{
    internal static void Run()
    {
        var options=WindowOptions.Default;
        options.IsVisible=false;options.Size=new Vector2D<int>(960,720);
        options.API=new GraphicsAPI(ContextAPI.OpenGL,ContextProfile.Core,ContextFlags.Default,new APIVersion(4,3));
        using var window=Window.Create(options);window.Initialize();window.GLContext!.MakeCurrent();
        Gl.Load(name=>window.GLContext.GetProcAddress(name));Gl.GenVertexArrays(1,out int vao);Gl.BindVertexArray(vao);
        int Compile(string fragment) {
            int Shader(uint type,string file) {
                int shader=Gl.CreateShader(type);Gl.ShaderSource(shader,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders",file)));
                Gl.CompileShader(shader);Gl.GetShaderiv(shader,0x8B81,out int okay);
                if(okay==0) throw new Exception(file+": "+Gl.ShaderLog(shader));return shader;
            }
            int v=Shader(0x8B31,"screen.vert"),f=Shader(0x8B30,fragment),p=Gl.CreateProgram();
            Gl.AttachShader(p,v);Gl.AttachShader(p,f);Gl.LinkProgram(p);Gl.GetProgramiv(p,0x8B82,out int okay);
            if(okay==0) throw new Exception(fragment+": "+Gl.ProgramLog(p));Gl.DeleteShader(v);Gl.DeleteShader(f);return p;
        }
        int Texture(int w,int h,float[]? data=null) {
            int id=Gl.GenTexture();Gl.BindTexture(0x0DE1,id);
            Gl.TexParameteri(0x0DE1,0x2801,0x2600);Gl.TexParameteri(0x0DE1,0x2800,0x2600);
            Gl.TexParameteri(0x0DE1,0x2802,0x812F);Gl.TexParameteri(0x0DE1,0x2803,0x812F);
            GCHandle pin=default;
            try {if(data!=null)pin=GCHandle.Alloc(data,GCHandleType.Pinned);
                Gl.TexImage2D(0x0DE1,0,0x881A,w,h,0,0x1908,0x1406,data==null?IntPtr.Zero:pin.AddrOfPinnedObject());}
            finally {if(pin.IsAllocated)pin.Free();}return id;
        }
        void Bind(int unit,int id) {Gl.ActiveTexture((uint)(0x84C0+unit));Gl.BindTexture(0x0DE1,id);}
        void Int(int p,string key,int value)=>Gl.Uniform1i(Gl.GetUniformLocation(p,key),value);
        foreach(string shader in new[] {"pathtrace.frag","probe.frag","exposure.frag","bloom_extract.frag","bloom_blur.frag","menu_background.frag"}) Compile(shader);
        int grade=Compile("color_grade.frag"),display=Compile("display.frag");
        int fbo=Gl.GenFramebuffer();Gl.BindFramebuffer(0x8D40,fbo);
        var input=new float[320*240*4];
        for(int i=0;i<input.Length;i+=4) {input[i]=(i/4%320)/319f;input[i+1]=.2f;input[i+2]=.7f;input[i+3]=1;}
        int source=Texture(320,240,input),graded=Texture(320,240),overlay=Texture(320,240,new float[input.Length]);
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,graded,0);Gl.Viewport(0,0,320,240);
        Gl.UseProgram(grade);Bind(0,source);Int(grade,"accumulation",0);Int(grade,"postEnabled",0);Gl.DrawArrays(4,0,3);
        var pixels=new float[input.Length];Gl.ReadPixels(320,240,pixels);
        if(Math.Abs(pixels[4*160]-MathF.Pow(input[4*160],1f/2.2f))>.002f) throw new Exception("GPU grade pass changed the bypass tone pipeline.");
        using(var processor=new NtscProcessor()) processor.Process(pixels,320,240,42,new NtscSettings());
        int effected=Texture(320,240,pixels),final=Texture(960,720);
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,final,0);Gl.Viewport(0,0,960,720);Gl.UseProgram(display);
        Bind(0,effected);Int(display,"accumulation",0);Bind(1,overlay);Int(display,"overlayTexture",1);
        Int(display,"paletteReduction",1);Int(display,"paletteSteps",16);Int(display,"ditheringEnabled",1);
        Gl.Uniform2f(Gl.GetUniformLocation(display,"windowSize"),960,720);Gl.DrawArrays(4,0,3);
        var result=new float[960*720*4];Gl.ReadPixels(960,720,result);
        for(int y=0;y<720;y++) for(int x=0;x<960;x++) for(int c=0;c<3;c++) {
            float actual=result[(y*960+x)*4+c];
            float reference=result[((y/3*3)*960+(x/3*3))*4+c];
            if(!float.IsFinite(actual)||Math.Abs(actual-reference)>.0001f) throw new Exception("NTSC/dither output varies within an enlarged game pixel.");
            if(Math.Abs(actual*15-MathF.Round(actual*15))>.01f) throw new Exception("NTSC output was not posterized by the final display pass.");
        }
        Console.WriteLine("PASS: all GLSL programs compile/link; GPU grade -> ntsc-rs -> 3x pixels -> dithering/posterization.");
    }
}

