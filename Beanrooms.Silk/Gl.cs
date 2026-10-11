using System.Runtime.InteropServices;
using System.Text;

namespace Beanrooms;

internal static class Gl
{
    static Func<string,IntPtr> getAddress=null!;
    static T Proc<T>(string name) where T:Delegate
    {
        IntPtr p=getAddress(name);
        if(p==IntPtr.Zero) throw new Exception("OpenGL function unavailable: "+name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateShaderD(uint type);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ShaderSourceD(int shader,int count,ref IntPtr text,ref int lengths);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ShaderIntD(int shader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GetIntD(int shader,uint name,out int result);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GetLogD(int objectId,int max,out int length,StringBuilder log);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int CreateProgramD();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void AttachShaderD(int program,int shader);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void LinkProgramD(int program);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenIdsD(int count,out int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindIdD(uint target,int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void BindVaoD(int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DeleteIdD(int count,ref int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TextureParameterD(uint target,uint name,int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TextureImageD(uint target,int level,int internalFormat,int width,int height,int border,uint format,uint type,IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void TextureSubImageD(uint target,int level,int x,int y,int width,int height,uint format,uint type,IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void GenerateMipmapD(uint target);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FramebufferTextureD(uint target,uint attachment,uint textarget,int texture,int level);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void UseProgramD(int program);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ActiveTextureD(uint unit);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetUniformD(int program,[MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform1D(int location,int value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform1FloatD(int location,float value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform2D(int location,float x,float y);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform3D(int location,float x,float y,float z);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform3ArrayD(int location,int count,[In] float[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void Uniform4ArrayD(int location,int count,[In] float[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void UniformIntArrayD(int location,int count,[In] int[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void UniformMatrix3D(int location,int count,[MarshalAs(UnmanagedType.I1)] bool transpose,[In] float[] matrix);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DrawArraysD(uint mode,int first,int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ViewportD(int x,int y,int w,int h);

    static CreateShaderD createShader=null!; static ShaderSourceD shaderSource=null!; static ShaderIntD compileShader=null!,deleteShader=null!;
    static GetIntD getShaderiv=null!,getProgramiv=null!; static GetLogD getShaderLog=null!,getProgramLog=null!;
    static CreateProgramD createProgram=null!; static AttachShaderD attachShader=null!; static LinkProgramD linkProgram=null!;
    static GenIdsD genTextures=null!,genFramebuffers=null!,genVaos=null!; static BindIdD bindTexture=null!,bindFramebuffer=null!; static BindVaoD bindVao=null!;
    static DeleteIdD deleteTextures=null!; static TextureParameterD texParameter=null!; static TextureImageD texImage=null!; static TextureSubImageD texSubImage=null!; static GenerateMipmapD generateMipmap=null!;
    static FramebufferTextureD framebufferTexture=null!; static UseProgramD useProgram=null!; static ActiveTextureD activeTexture=null!;
    static GetUniformD getUniform=null!; static Uniform1D uniform1=null!; static Uniform1FloatD uniform1Float=null!; static Uniform2D uniform2=null!; static Uniform3D uniform3=null!; static UniformMatrix3D uniformMatrix3=null!;
    static Uniform3ArrayD uniform3Array=null!; static Uniform4ArrayD uniform4Array=null!; static UniformIntArrayD uniformIntArray=null!;
    static DrawArraysD drawArrays=null!; static ViewportD viewport=null!;
    internal static void Load(Func<string,IntPtr> address)
    {
        getAddress=address;
        createShader=Proc<CreateShaderD>("glCreateShader"); shaderSource=Proc<ShaderSourceD>("glShaderSource"); compileShader=Proc<ShaderIntD>("glCompileShader"); deleteShader=Proc<ShaderIntD>("glDeleteShader");
        getShaderiv=Proc<GetIntD>("glGetShaderiv"); getProgramiv=Proc<GetIntD>("glGetProgramiv"); getShaderLog=Proc<GetLogD>("glGetShaderInfoLog"); getProgramLog=Proc<GetLogD>("glGetProgramInfoLog");
        createProgram=Proc<CreateProgramD>("glCreateProgram"); attachShader=Proc<AttachShaderD>("glAttachShader"); linkProgram=Proc<LinkProgramD>("glLinkProgram");
        genTextures=Proc<GenIdsD>("glGenTextures"); genFramebuffers=Proc<GenIdsD>("glGenFramebuffers"); genVaos=Proc<GenIdsD>("glGenVertexArrays"); bindTexture=Proc<BindIdD>("glBindTexture"); bindFramebuffer=Proc<BindIdD>("glBindFramebuffer"); bindVao=Proc<BindVaoD>("glBindVertexArray"); deleteTextures=Proc<DeleteIdD>("glDeleteTextures");
        texParameter=Proc<TextureParameterD>("glTexParameteri"); texImage=Proc<TextureImageD>("glTexImage2D"); texSubImage=Proc<TextureSubImageD>("glTexSubImage2D"); generateMipmap=Proc<GenerateMipmapD>("glGenerateMipmap"); framebufferTexture=Proc<FramebufferTextureD>("glFramebufferTexture2D");
        useProgram=Proc<UseProgramD>("glUseProgram"); activeTexture=Proc<ActiveTextureD>("glActiveTexture"); getUniform=Proc<GetUniformD>("glGetUniformLocation"); uniform1=Proc<Uniform1D>("glUniform1i"); uniform1Float=Proc<Uniform1FloatD>("glUniform1f"); uniform2=Proc<Uniform2D>("glUniform2f"); uniform3=Proc<Uniform3D>("glUniform3f"); uniform3Array=Proc<Uniform3ArrayD>("glUniform3fv"); uniform4Array=Proc<Uniform4ArrayD>("glUniform4fv"); uniformIntArray=Proc<UniformIntArrayD>("glUniform1iv"); uniformMatrix3=Proc<UniformMatrix3D>("glUniformMatrix3fv"); drawArrays=Proc<DrawArraysD>("glDrawArrays"); viewport=Proc<ViewportD>("glViewport");
    }
    internal static int CreateShader(uint type)=>createShader(type);
    internal static void ShaderSource(int shader,string source)
    { IntPtr p=Marshal.StringToHGlobalAnsi(source); try { int len=Encoding.ASCII.GetByteCount(source); shaderSource(shader,1,ref p,ref len); } finally { Marshal.FreeHGlobal(p); } }
    internal static void CompileShader(int shader)=>compileShader(shader);
    internal static void GetShaderiv(int shader,uint name,out int result)=>getShaderiv(shader,name,out result);
    internal static void GetProgramiv(int program,uint name,out int result)=>getProgramiv(program,name,out result);
    internal static string ShaderLog(int shader) { var sb=new StringBuilder(8192); getShaderLog(shader,sb.Capacity,out _,sb); return sb.ToString(); }
    internal static string ProgramLog(int program) { var sb=new StringBuilder(8192); getProgramLog(program,sb.Capacity,out _,sb); return sb.ToString(); }
    internal static int CreateProgram()=>createProgram();
    internal static void AttachShader(int program,int shader)=>attachShader(program,shader);
    internal static void LinkProgram(int program)=>linkProgram(program);
    internal static void DeleteShader(int shader)=>deleteShader(shader);
    internal static int GenTexture() { genTextures(1,out int id); return id; }
    internal static int GenFramebuffer() { genFramebuffers(1,out int id); return id; }
    internal static void GenVertexArrays(int count,out int id)=>genVaos(count,out id);
    internal static void BindVertexArray(int id)=>bindVao(id);
    internal static void BindTexture(uint target,int id)=>bindTexture(target,id);
    internal static void BindFramebuffer(uint target,int id)=>bindFramebuffer(target,id);
    internal static void DeleteTexture(int id)=>deleteTextures(1,ref id);
    internal static void TexParameteri(uint target,uint name,int value)=>texParameter(target,name,value);
    internal static void TexImage2D(uint target,int level,int internalFormat,int width,int height,int border,uint format,uint type,IntPtr pixels)=>texImage(target,level,internalFormat,width,height,border,format,type,pixels);
    internal static void TexSubImage2D(uint target,int level,int x,int y,int width,int height,uint format,uint type,IntPtr pixels)=>texSubImage(target,level,x,y,width,height,format,type,pixels);
    internal static void GenerateMipmap(uint target)=>generateMipmap(target);
    internal static void FramebufferTexture2D(uint target,uint attachment,uint texTarget,int tex,int level)=>framebufferTexture(target,attachment,texTarget,tex,level);
    internal static void UseProgram(int program)=>useProgram(program);
    internal static void ActiveTexture(uint unit)=>activeTexture(unit);
    internal static int GetUniformLocation(int program,string name)=>getUniform(program,name);
    internal static void Uniform1i(int location,int value)=>uniform1(location,value);
    internal static void Uniform1f(int location,float value)=>uniform1Float(location,value);
    internal static void Uniform2f(int location,float x,float y)=>uniform2(location,x,y);
    internal static void Uniform3f(int location,float x,float y,float z)=>uniform3(location,x,y,z);
    internal static void Uniform3fv(int location,int count,float[] values)=>uniform3Array(location,count,values);
    internal static void Uniform4fv(int location,int count,float[] values)=>uniform4Array(location,count,values);
    internal static void Uniform1iv(int location,int count,int[] values)=>uniformIntArray(location,count,values);
    internal static void UniformMatrix3fv(int location,int count,bool transpose,float[] matrix)=>uniformMatrix3(location,count,transpose,matrix);
    internal static void DrawArrays(uint mode,int first,int count)=>drawArrays(mode,first,count);
    internal static void Viewport(int x,int y,int w,int h)=>viewport(x,y,w,h);
}
