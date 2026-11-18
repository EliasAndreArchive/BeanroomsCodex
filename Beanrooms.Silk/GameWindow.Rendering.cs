using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Beanrooms;

internal sealed partial class GameWindow
{
    int Compile(string vertex, string fragment)
    {
        int vs = Gl.CreateShader(0x8B31), fs = Gl.CreateShader(0x8B30);
        CompileOne(vs,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders",vertex)),vertex);
        CompileOne(fs,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"shaders",fragment)),fragment);
        int program = Gl.CreateProgram();
        Gl.AttachShader(program,vs); Gl.AttachShader(program,fs); Gl.LinkProgram(program);
        Gl.GetProgramiv(program,0x8B82,out int okay);
        if (okay == 0) throw new Exception("Shader link failed: " + Gl.ProgramLog(program));
        Gl.DeleteShader(vs); Gl.DeleteShader(fs);
        return program;
    }
    static void CompileOne(int shader,string source,string name)
    {
        Gl.ShaderSource(shader,source); Gl.CompileShader(shader);
        Gl.GetShaderiv(shader,0x8B81,out int okay);
        if (okay == 0) throw new Exception(name + " shader error:\n" + Gl.ShaderLog(shader));
    }

    int LoadImage(string filename)
    {
        string path = Path.Combine(AppContext.BaseDirectory,"assets",filename);
        if (!File.Exists(path)) throw new FileNotFoundException("Missing texture",path);
        using var original = new Bitmap(path);
        using var image = new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(image)) g.DrawImageUnscaled(original,0,0);
        var data = image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        int id = Gl.GenTexture();
        Gl.BindTexture(0x0DE1,id);
        // Point-sample both texels and mip levels for the low-resolution look.
        Gl.TexParameteri(0x0DE1,0x2801,0x2700); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x2901); Gl.TexParameteri(0x0DE1,0x2803,0x2901);
        Gl.TexImage2D(0x0DE1,0,0x8C43,image.Width,image.Height,0,0x80E1,0x1401,data.Scan0);
        Gl.GenerateMipmap(0x0DE1);
        image.UnlockBits(data);
        return id;
    }

    void CreateRenderTarget()
    {
        if (renderTexture != 0) Gl.DeleteTexture(renderTexture);
        renderTexture = Gl.GenTexture(); Gl.BindTexture(0x0DE1,renderTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2703); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        Gl.TexImage2D(0x0DE1,0,0x881A,width,height,0,0x1908,0x1406,IntPtr.Zero);
        Gl.GenerateMipmap(0x0DE1);
        CreateBloomTargets();
    }

    void CreateBloomTargets()
    {
        int w=Math.Max(1,width/2),h=Math.Max(1,height/2);
        if(bloomA!=0) Gl.DeleteTexture(bloomA);
        if(bloomB!=0) Gl.DeleteTexture(bloomB);
        bloomA=CreatePostTexture(w,h,IntPtr.Zero);
        bloomB=CreatePostTexture(w,h,IntPtr.Zero);
    }
    void CreateExposureTargets()
    {
        float[] neutral=[1f,0f,0f,1f];
        var pin=GCHandle.Alloc(neutral,GCHandleType.Pinned);
        try
        {
            exposureA=CreatePostTexture(1,1,pin.AddrOfPinnedObject());
            exposureB=CreatePostTexture(1,1,pin.AddrOfPinnedObject());
        }
        finally { pin.Free(); }
        exposureCurrent=0;
        lastExposureTime=watch.Elapsed.TotalSeconds;
    }
    static int CreatePostTexture(int width,int height,IntPtr initial)
    {
        int texture=Gl.GenTexture();Gl.BindTexture(0x0DE1,texture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2601);Gl.TexParameteri(0x0DE1,0x2800,0x2601);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F);Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        Gl.TexImage2D(0x0DE1,0,0x881A,width,height,0,0x1908,0x1406,initial);
        return texture;
    }

    void CreateProbeTargets()
    {
        int wanted=menu.ProbeQuality switch { 0=>48, 1=>96, 2=>144, _=>192 };
        if(probeSize==wanted && probeDirect!=0) return;
        if(probeDirect!=0) Gl.DeleteTexture(probeDirect);
        if(probeA!=0) Gl.DeleteTexture(probeA);
        if(probeB!=0) Gl.DeleteTexture(probeB);
        probeSize=wanted;
        probeDirect=CreatePostTexture(wanted,wanted,IntPtr.Zero);
        probeA=CreatePostTexture(wanted,wanted,IntPtr.Zero);
        probeB=CreatePostTexture(wanted,wanted,IntPtr.Zero);
        probeResult=probeDirect;
        probeDirty=true;
    }

    int ProbeSettingsSignature()
    {
        var hash=new HashCode();
        hash.Add(menu.ProbeQuality);hash.Add(menu.ProbeBounces);
        hash.Add(menu.Lights);hash.Add(menu.LightIntensity);hash.Add(menu.BounceStrength);
        if(network.Connected)
        {
            foreach(CubeState cube in networkCubes)
            {
                if(!cube.Emissive) continue;
                hash.Add((int)MathF.Round(cube.X*20f));hash.Add((int)MathF.Round(cube.Y*20f));
                hash.Add((int)MathF.Round(cube.Z*20f));hash.Add(cube.HalfSize);
                hash.Add(cube.R);hash.Add(cube.G);hash.Add(cube.B);
            }
        }
        else
        {
            foreach(RigidCube cube in physics.Bodies)
            {
                if(!cube.Emissive) continue;
                hash.Add((int)MathF.Round(cube.Position.X*20f));
                hash.Add((int)MathF.Round(cube.Position.Y*20f));
                hash.Add((int)MathF.Round(cube.Position.Z*20f));
                hash.Add(cube.HalfSize);hash.Add(cube.Color);
            }
        }
        return hash.ToHashCode();
    }

    float CubeVisualScale(int index,int id,double now)
    {
        if(cubeVisualIds[index]!=id)
        {
            cubeVisualIds[index]=id;
            cubeVisualBirth[index]=now;
        }
        float t=Math.Clamp((float)((now-cubeVisualBirth[index])/0.32),0f,1f);
        float eased=1f-(1f-t)*(1f-t)*(1f-t);
        return 0.12f+0.88f*eased;
    }

    void UploadObjects(int program,bool animate)
    {
        RigidCube[] localCubes=network.Connected?[]:physics.Bodies.ToArray();
        int cubeCount=network.Connected?networkCubes.Length:localCubes.Length;
        int bodyCount=cubeCount;
        if(cubeVisualIds.Length<cubeCount)
        {
            Array.Resize(ref cubeVisualIds,cubeCount);
            Array.Resize(ref cubeVisualBirth,cubeCount);
        }
        if(cubeVisualNetworkMode!=network.Connected || cubeCount==0)
        {
            Array.Clear(cubeVisualIds);
            cubeVisualNetworkMode=network.Connected;
        }
        Uniform1(program,"objectCount",bodyCount);
        if(bodyCount==0) return;
        if(objectTexels.Length<bodyCount*12) Array.Resize(ref objectTexels,bodyCount*12);
        float[] texels=objectTexels;
        double now=watch.Elapsed.TotalSeconds;
        for(int i=0;i<cubeCount;i++)
        {
            int j=i*12;
            if(network.Connected)
            {
                CubeState b=networkCubes[i];
                texels[j]=b.X; texels[j+1]=b.Y; texels[j+2]=b.Z;
                texels[j+3]=b.HalfSize*(animate?CubeVisualScale(i,b.Id,now):1f);
                texels[j+4]=b.Qx;texels[j+5]=b.Qy;texels[j+6]=b.Qz;texels[j+7]=b.Qw;
                texels[j+8]=b.R;texels[j+9]=b.G;texels[j+10]=b.B;
                texels[j+11]=b.Emissive?1f:0f;
            }
            else
            {
                RigidCube b=localCubes[i];
                texels[j]=b.Position.X; texels[j+1]=b.Position.Y; texels[j+2]=b.Position.Z;
                texels[j+3]=b.HalfSize*(animate?CubeVisualScale(i,b.Id,now):1f);
                texels[j+4]=b.Rotation.X; texels[j+5]=b.Rotation.Y; texels[j+6]=b.Rotation.Z; texels[j+7]=b.Rotation.W;
                texels[j+8]=b.Color.X; texels[j+9]=b.Color.Y; texels[j+10]=b.Color.Z;
                texels[j+11]=b.Emissive?1f:0f;
            }
        }
        int textureUnit=program==probeProgram?4:9;
        Bind(textureUnit,objectTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        var handle=GCHandle.Alloc(texels,GCHandleType.Pinned);
        try
        {
            if(objectTextureHeight!=bodyCount)
            {
                Gl.TexImage2D(0x0DE1,0,0x8814,3,bodyCount,0,0x1908,0x1406,handle.AddrOfPinnedObject());
                objectTextureHeight=bodyCount;
            }
            else Gl.TexSubImage2D(0x0DE1,0,0,0,3,bodyCount,0x1908,0x1406,handle.AddrOfPinnedObject());
        }
        finally { handle.Free(); }
        Uniform1(program,"objectMap",textureUnit);
    }

    void RenderProbes()
    {
        CreateProbeTargets();
        Gl.BindFramebuffer(0x8D40,framebuffer);
        Gl.Viewport(0,0,probeSize,probeSize);
        Gl.UseProgram(probeProgram);
        Bind(0,worldTexture);Bind(1,fixtureTexture);
        Bind(2,0);Bind(3,0);
        Uniform1(probeProgram,"worldGrid",0);
        Uniform1(probeProgram,"fixtureMap",1);
        Uniform1(probeProgram,"directTex",2);
        Uniform1(probeProgram,"previousTex",3);
        Uniform1(probeProgram,"probeSize",probeSize);
        Uniform1(probeProgram,"probeRadius",menu.ProbeQuality==0?1:2);
        Uniform1(probeProgram,"probeLightSamples",menu.ProbeQuality==0?1:menu.ProbeQuality==1?2:4);
        Uniform1(probeProgram,"lightsEnabled",menu.Lights?1:0);
        Uniform1f(probeProgram,"lightIntensity",menu.LightIntensity);
        Uniform1f(probeProgram,"bounceStrength",menu.BounceStrength);
        UploadObjects(probeProgram,false);
        Uniform1(probeProgram,"passIndex",0);
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,probeDirect,0);
        Gl.DrawArrays(0x0004,0,3);
        int previous=probeDirect;
        for(int bounce=0;bounce<menu.ProbeBounces;bounce++)
        {
            int target=(bounce&1)==0?probeA:probeB;
            Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,target,0);
            Bind(2,probeDirect);Bind(3,previous);
            Uniform1(probeProgram,"passIndex",1);
            Gl.DrawArrays(0x0004,0,3);
            previous=target;
        }
        probeResult=previous;
        probeDirty=false;
    }

    void ResizeRender(int w,int h) { width=w; height=h; CreateRenderTarget(); }
}
