using System.IO;
using System.Diagnostics;
using System.Numerics;
using System.Drawing;

namespace Beanrooms;

internal sealed class GameAudio : IDisposable
{
    AudioSystem? system;
    AudioClip? spawnClip,clickClip,hoverClip,lampClip;
    AudioVoice? spawn,click,hover;
    LoopingSound[]? lamps;
    LoopingSound? monster;
    PhysicalSounds? physical;
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastClick = -1, lastHover = -1;
    bool available;
    VercidiumAcoustics? acoustics;

    internal GameAudio()
    {
        try
        {
            system=new AudioSystem();
            spawnClip=system.Load("spawn-boom.mp3");spawn=new AudioVoice(spawnClip);spawn.Set(0.8);
            clickClip=system.Load("ui-click.wav");click=new AudioVoice(clickClip);click.Set(0.55);
            hoverClip=system.Load("ui-hover.wav");hover=new AudioVoice(hoverClip);hover.Set(0.35);
            AudioClip loadedLamp=system.Load("lamp-buzz.mp3");
            lampClip=loadedLamp;
            lamps=Enumerable.Range(0,4).Select(_=>new LoopingSound(loadedLamp)).ToArray();
            monster=new(system,"monster.wav");
            physical=new PhysicalSounds(system);
            available=true;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"Game audio could not start: {ex}");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"audio-error.log"),ex.ToString()); }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
            Dispose();
        }
    }

    internal void PlaySpawn()
    {
        if(!available) return;
        spawn!.Play();
    }

    internal void PlayClick()
    {
        double now=clock.Elapsed.TotalSeconds;
        if(!available || now-lastClick<0.065) return;
        lastClick=now;
        click!.Play();
    }

    internal void PlayHover()
    {
        double now=clock.Elapsed.TotalSeconds;
        if(!available || now-lastHover<0.055) return;
        lastHover=now;
        hover!.Play();
    }

    internal void SetWorld(IReadOnlyList<RectangleF> walls)
    {
        acoustics?.Dispose();
        acoustics=null;
        try { acoustics=new VercidiumAcoustics(walls,Math.Max(1,system?.Reverb?.GroupCapacity??1)); }
        catch(Exception ex) { Console.Error.WriteLine($"Vercidium audio unavailable: {ex.Message}"); }
    }

    internal void UpdateMenu(float dt)
    {
        if(!available) return;
        foreach(LoopingSound lamp in lamps!)
        { lamp.SetTarget(0);lamp.Update(dt); }
        monster!.SetTarget(0);
        monster.Update(dt);
        physical!.Update(Vector3.Zero,0,new FootstepSource(0,Vector3.Zero,0,0,0),
            [],[],[],dt,_=>VercidiumAcoustics.Filter.Clear,_=>-1,[]);
    }

    internal void Update(Vector3 listener,float yaw,IReadOnlyList<Vector2> fixtures,bool lightsOn,
        MonsterState? eye,bool monsterOn,float speed,float sprintBlend,float crouchBlend,float dt,
        IReadOnlyList<FootstepSource> remotePlayers,IReadOnlyList<CubeSoundSource> cubes,
        IReadOnlyList<CubeImpactEvent> impacts,IReadOnlyList<RectangleF> walls)
    {
        if(!available) return;
        Vector2 listenerXZ=new(listener.X,listener.Z);
        Vector2 right=new(MathF.Cos(yaw),-MathF.Sin(yaw));
        var nearbyLamps=lightsOn?fixtures.Select(fixture=>
            (Position:fixture,DistanceSquared:Vector2.DistanceSquared(fixture,listenerXZ)))
            .Where(fixture=>fixture.DistanceSquared<400f)
            .OrderBy(fixture=>fixture.DistanceSquared).Take(lamps!.Length).ToArray():[];
        List<AcousticSource> sources=[];
        if(monsterOn&&eye!=null) sources.Add(new(AcousticSourceIds.Monster,new Vector3(eye.X,eye.Y,eye.Z)));
        foreach(var lamp in nearbyLamps)
            sources.Add(new(AcousticSourceIds.Lamp(lamp.Position),new Vector3(lamp.Position.X,3.15f,lamp.Position.Y)));
        foreach(FootstepSource player in remotePlayers.OrderBy(player=>Vector3.DistanceSquared(player.Position,listener)).Take(8))
            sources.Add(new(AcousticSourceIds.Player(player.Id),player.Position-new Vector3(0f,1.4f,0f)));
        foreach(CubeSoundSource cube in cubes.OrderBy(cube=>Vector3.DistanceSquared(cube.Position,listener)).Take(8))
            sources.Add(new(AcousticSourceIds.Cube(cube.Id),cube.Position));
        VercidiumAcoustics.Filter FilterFor(int id)=>acoustics?.FilterFor(id)??VercidiumAcoustics.Filter.Clear;
        int GroupFor(int id)=>acoustics?.GroupFor(id)??-1;
        if(acoustics!=null)
        {
            try
            {
                acoustics.Update(listener,sources);
                system?.Reverb?.SetRoom(acoustics.Room);
                system?.Reverb?.SetGroups(acoustics.Groups);
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine($"Vercidium audio stopped: {ex.Message}");
                acoustics.Dispose();acoustics=null;
            }
        }
        VercidiumAcoustics.Filter monsterFilter=FilterFor(AcousticSourceIds.Monster);
        monster!.SetHighFrequencyGain(monsterFilter.HighRatio);
        monster.SetReverbGroup(GroupFor(AcousticSourceIds.Monster));
        for(int i=0;i<lamps!.Length;i++)
        {
            if(i>=nearbyLamps.Length) { lamps[i].SetTarget(0f);continue; }
            Vector2 offset=nearbyLamps[i].Position-listenerXZ;
            float distance=MathF.Sqrt(nearbyLamps[i].DistanceSquared+2.16f);
            float gain=0.22f/(1f+nearbyLamps[i].DistanceSquared/28f);
            float pan=Vector2.Dot(offset,right)/MathF.Max(distance,0.1f)*0.8f;
            VercidiumAcoustics.Filter lampFilter=FilterFor(AcousticSourceIds.Lamp(nearbyLamps[i].Position));
            lamps[i].SetHighFrequencyGain(lampFilter.HighRatio);
            lamps[i].SetReverbGroup(GroupFor(AcousticSourceIds.Lamp(nearbyLamps[i].Position)));
            lamps[i].SetTarget(gain*lampFilter.Gain,Math.Clamp(pan,-0.85f,0.85f));
        }

        if(!monsterOn || eye==null) monster!.SetTarget(0);
        else
        {
            Vector2 toMonster=new(eye.X-listener.X,eye.Z-listener.Z);
            float distance=toMonster.Length();
            float proximity=Math.Clamp((42f-distance)/39f,0f,1f);
            monster.SetTarget(0.8f*proximity*proximity*monsterFilter.Gain,
                distance>0.1f ? Math.Clamp(Vector2.Dot(toMonster,right)/distance*0.8f,-0.85f,0.85f) : 0f);
        }
        foreach(LoopingSound lamp in lamps) lamp.Update(dt);
        monster!.Update(dt);
        physical!.Update(listener,yaw,new FootstepSource(0,listener,speed,sprintBlend,crouchBlend),
            remotePlayers,cubes,impacts,dt,FilterFor,GroupFor,walls);
    }

    public void Dispose()
    {
        acoustics?.Dispose();acoustics=null;
        spawn?.Dispose();click?.Dispose();hover?.Dispose();
        if(lamps!=null) foreach(LoopingSound lamp in lamps) lamp.Dispose();
        monster?.Dispose();physical?.Dispose();
        lampClip?.Dispose();spawnClip?.Dispose();clickClip?.Dispose();hoverClip?.Dispose();system?.Dispose();
        available=false;
    }
}
