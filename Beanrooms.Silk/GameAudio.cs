using System.IO;
using System.Diagnostics;
using System.Numerics;

namespace Beanrooms;

internal sealed class GameAudio : IDisposable
{
    AudioSystem? system;
    AudioClip? spawnClip,clickClip,hoverClip;
    AudioVoice? spawn,click,hover;
    LoopingSound? lamps,monster,walking,running,crouching;
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastClick = -1, lastHover = -1;
    bool available;

    internal GameAudio()
    {
        try
        {
            system=new AudioSystem();
            spawnClip=system.Load("spawn-boom.mp3");spawn=new AudioVoice(spawnClip);spawn.Set(0.8);
            clickClip=system.Load("ui-click.wav");click=new AudioVoice(clickClip);click.Set(0.55);
            hoverClip=system.Load("ui-hover.wav");hover=new AudioVoice(hoverClip);hover.Set(0.35);
            lamps=new(system,"lamp-buzz.mp3");monster=new(system,"monster.wav");
            walking=new(system,"walking.mp3");running=new(system,"running.wav");
            crouching=new(system,"crouch-walking.mp3");
            available=true;
        }
        catch(Exception) { Dispose(); }
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

    internal void Update(Vector3 listener,float yaw,IReadOnlyList<Vector2> fixtures,bool lightsOn,
        MonsterState? eye,bool monsterOn,float speed,float sprintBlend,float crouchBlend,float dt)
    {
        if(!available) return;
        Vector2 listenerXZ=new(listener.X,listener.Z);
        Vector2 right=new(MathF.Cos(yaw),-MathF.Sin(yaw));
        float lampEnergy=0f, lampPan=0f;
        if(lightsOn) foreach(Vector2 fixture in fixtures)
        {
            Vector2 offset=fixture-listenerXZ;
            float distanceSquared=offset.LengthSquared();
            if(distanceSquared>900f) continue;
            float strength=1f/(1f+distanceSquared/40f);
            lampEnergy+=strength;
            lampPan+=strength*Vector2.Dot(offset,right)/MathF.Sqrt(MathF.Max(distanceSquared,1f));
        }
        lamps!.SetTarget(Math.Clamp(lampEnergy*0.11f,0f,0.32f),
            lampEnergy>0f ? Math.Clamp(lampPan/lampEnergy*0.75f,-0.85f,0.85f) : 0f);

        float motion=Math.Clamp(speed/1.2f,0f,1f);
        float crouch=Math.Clamp(crouchBlend,0f,1f);
        float sprint=Math.Clamp(sprintBlend,0f,1f)*(1f-crouch);
        walking!.SetTarget(0.42f*motion*(1f-sprint)*(1f-crouch));
        running!.SetTarget(0.46f*motion*sprint);
        crouching!.SetTarget(0.40f*motion*crouch);

        if(!monsterOn || eye==null) monster!.SetTarget(0);
        else
        {
            Vector2 toMonster=new(eye.X-listener.X,eye.Z-listener.Z);
            float distance=toMonster.Length();
            float proximity=Math.Clamp((42f-distance)/39f,0f,1f);
            monster!.SetTarget(0.8f*proximity*proximity,
                distance>0.1f ? Math.Clamp(Vector2.Dot(toMonster,right)/distance*0.8f,-0.85f,0.85f) : 0f);
        }
        lamps.Update(dt); monster!.Update(dt); walking.Update(dt); running.Update(dt); crouching.Update(dt);
    }

    public void Dispose()
    {
        spawn?.Dispose();click?.Dispose();hover?.Dispose();
        lamps?.Dispose();monster?.Dispose();walking?.Dispose();running?.Dispose();crouching?.Dispose();
        spawnClip?.Dispose();clickClip?.Dispose();hoverClip?.Dispose();system?.Dispose();
        available=false;
    }
}
