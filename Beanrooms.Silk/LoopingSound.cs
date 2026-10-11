using System.Diagnostics;
using Silk.NET.OpenAL;
using NLayer;

namespace Beanrooms;

internal unsafe sealed class AudioSystem : IDisposable
{
    internal readonly AL Al;
    readonly ALContext contextApi;
    readonly Device* device;
    readonly Context* context;
    internal AudioSystem()
    {
        contextApi=ALContext.GetApi();
        device=contextApi.OpenDevice("");
        if(device==null) throw new Exception("No OpenAL audio device");
        context=contextApi.CreateContext(device,null);
        if(context==null || !contextApi.MakeContextCurrent(context)) throw new Exception("Could not start OpenAL audio");
        Al=AL.GetApi();
    }
    internal AudioClip Load(string name)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"assets",name);
        short[] samples;
        int sampleRate,channels;
        if(Path.GetExtension(path).Equals(".mp3",StringComparison.OrdinalIgnoreCase))
        {
            using var file=new MpegFile(path);
            sampleRate=file.SampleRate;channels=file.Channels;
            using var pcm=new MemoryStream();
            using var writer=new BinaryWriter(pcm);
            float[] block=new float[16384];
            int count;
            while((count=file.ReadSamples(block,0,block.Length))>0)
                for(int i=0;i<count;i++) writer.Write((short)Math.Clamp((int)(block[i]*32767f),-32768,32767));
            samples=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,short>(pcm.ToArray()).ToArray();
        }
        else
        {
            using var stream=File.OpenRead(path);
            using var reader=new BinaryReader(stream);
            if(reader.ReadUInt32()!=0x46464952) throw new InvalidDataException("Invalid WAV");
            reader.ReadUInt32();
            if(reader.ReadUInt32()!=0x45564157) throw new InvalidDataException("Invalid WAV");
            int format=0,bits=0;channels=0;sampleRate=0;samples=[];
            while(stream.Position+8<=stream.Length)
            {
                uint chunk=reader.ReadUInt32();int length=reader.ReadInt32();
                long end=stream.Position+length;
                if(chunk==0x20746d66)
                {
                    format=reader.ReadUInt16();channels=reader.ReadUInt16();sampleRate=reader.ReadInt32();
                    reader.ReadInt32();reader.ReadUInt16();bits=reader.ReadUInt16();
                }
                else if(chunk==0x61746164)
                {
                    byte[] data=reader.ReadBytes(length);
                    if(format==1 && bits==16) samples=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,short>(data).ToArray();
                    else if(format==3 && bits==32)
                    {
                        float[] floats=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(data).ToArray();
                        samples=Array.ConvertAll(floats,v=>(short)Math.Clamp((int)(v*32767f),-32768,32767));
                    }
                    else throw new InvalidDataException($"Unsupported WAV format {format}/{bits}");
                }
                stream.Position=Math.Min(stream.Length,end+(length&1));
            }
        }
        if(channels is not (1 or 2) || sampleRate<=0 || samples.Length==0) throw new InvalidDataException("Invalid audio clip: "+name);
        uint buffer=Al.GenBuffer();
        Al.BufferData(buffer,channels==1?BufferFormat.Mono16:BufferFormat.Stereo16,samples,sampleRate);
        return new AudioClip(this,buffer,samples.Length/(double)(sampleRate*channels));
    }
    public void Dispose()
    {
        Al.Dispose();
        contextApi.MakeContextCurrent((Context*)null);
        contextApi.DestroyContext(context);
        contextApi.CloseDevice(device);
        contextApi.Dispose();
    }
}
internal sealed class AudioClip(AudioSystem system,uint buffer,double duration) : IDisposable
{
    internal AudioSystem System { get; }=system;
    internal uint Buffer { get; }=buffer;
    internal double Duration { get; }=duration;
    public void Dispose() => System.Al.DeleteBuffer(Buffer);
}
internal sealed class AudioVoice : IDisposable
{
    readonly AudioClip clip;
    readonly uint source;
    internal AudioVoice(AudioClip clip)
    {
        this.clip=clip;
        source=clip.System.Al.GenSource();
        clip.System.Al.SetSourceProperty(source,SourceInteger.Buffer,clip.Buffer);
        clip.System.Al.SetSourceProperty(source,SourceBoolean.SourceRelative,true);
    }
    internal void Play() { Stop();clip.System.Al.SourcePlay(source); }
    internal void Stop() => clip.System.Al.SourceStop(source);
    internal void Set(double gain,double pan=0)
    {
        clip.System.Al.SetSourceProperty(source,SourceFloat.Gain,(float)Math.Clamp(gain,0,1));
        clip.System.Al.SetSourceProperty(source,SourceVector3.Position,(float)Math.Clamp(pan,-1,1),0f,-1f);
    }
    public void Dispose() { Stop();clip.System.Al.DeleteSource(source); }
}

// Keep the original two-voice, equal-power crossfade and eased volume targets.
internal sealed class LoopingSound : IDisposable
{
    const double CrossfadeSeconds=0.4;
    readonly AudioClip clip;
    readonly AudioVoice[] voices;
    readonly Stopwatch clock=Stopwatch.StartNew();
    int active;
    bool crossing;
    double startedAt,crossingAt,volume,balance,targetVolume,targetBalance;
    internal LoopingSound(AudioSystem system,string filename)
    {
        clip=system.Load(filename);
        voices=[new AudioVoice(clip),new AudioVoice(clip)];
        voices[0].Set(0);
        voices[0].Play();startedAt=clock.Elapsed.TotalSeconds;
    }
    void BeginCrossfade()
    {
        int next=1-active;
        voices[next].Set(0,balance);
        voices[next].Play();
        crossingAt=clock.Elapsed.TotalSeconds;
        crossing=true;
    }
    internal void SetTarget(double gain,double pan=0)
    {
        targetVolume=Math.Clamp(gain,0,1);
        targetBalance=Math.Clamp(pan,-1,1);
    }
    internal void Update(float dt)
    {
        double blend=1-Math.Exp(-Math.Clamp(dt,0f,0.1f)*9.0);
        volume+=(targetVolume-volume)*blend;
        balance+=(targetBalance-balance)*blend;
        double now=clock.Elapsed.TotalSeconds;
        if(!crossing && now-startedAt>=Math.Max(0.01,clip.Duration-CrossfadeSeconds)) BeginCrossfade();
        if(crossing)
        {
            double t=Math.Clamp((now-crossingAt)/CrossfadeSeconds,0,1);
            voices[active].Set(volume*Math.Cos(t*Math.PI*0.5),balance);
            voices[1-active].Set(volume*Math.Sin(t*Math.PI*0.5),balance);
            if(t>=1)
            {
                voices[active].Stop();voices[active].Set(0);
                active=1-active;crossing=false;startedAt=crossingAt;
            }
        }
        else voices[active].Set(volume,balance);
    }
    public void Dispose() { foreach(var voice in voices) voice.Dispose();clip.Dispose(); }
}
