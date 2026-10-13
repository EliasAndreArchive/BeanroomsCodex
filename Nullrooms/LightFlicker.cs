namespace Beanrooms;

// Fixed world behavior shared by the solo client and the Photon authority.
internal static class FlickerProfile
{
    internal const float MinorInterval=34f, MinorChance=.36f, MinorDuration=1.5f;
    internal const float EventInterval=112f, EventDuration=4.5f;
    internal const float EventSpeedMin=5f, EventSpeedMax=14f, EventOffChance=.46f;
    internal const float BlackoutChance=.12f;
    internal const float ChaseFogMax=.1f, FogRise=1.8f, FogFall=.9f;
    internal const float ChaseRedChance=.8f;
}

// Computes one on/off state per fixture before the lighting pass. All lighting
// paths use the same fixture texture, so the panel and the light it casts agree.
internal sealed class LightFlicker
{
    double globalStarted=double.NegativeInfinity;
    double chaseStarted=double.NegativeInfinity;
    double nextRandomEvent;
    bool blackout;
    bool blackoutAfterEvent;

    internal LightFlicker(double now)
    {
        Schedule(now);
    }

    internal void Trigger(double now,bool blackoutAfter=false)
    {
        // A new flicker restores a blacked out room; only a random event can
        // schedule the next blackout.
        bool restoring=blackout || blackoutAfterEvent;
        blackout=false;
        blackoutAfterEvent=!restoring && blackoutAfter;
        globalStarted=now;
        Schedule(now);
    }

    internal void Restore(double now,bool isBlackout,bool afterEvent)
    {
        blackout=isBlackout;
        blackoutAfterEvent=afterEvent;
        if(isBlackout) globalStarted=double.NegativeInfinity;
    }

    internal void UpdateRandom(double now,bool online)
    {
        if(!online && now>=nextRandomEvent)
            Trigger(now,Random.Shared.NextSingle()<FlickerProfile.BlackoutChance);
    }

    internal void UpdateChase(double now,bool locked)
    {
        if(locked)
        {
            if(double.IsNegativeInfinity(chaseStarted)) chaseStarted=now;
        }
        else chaseStarted=double.NegativeInfinity;
    }

    internal double EventAge(double now)
    {
        double age=now-globalStarted;
        return age>=0 && age<FlickerProfile.EventDuration ? age : -1;
    }

    internal bool IsOn(int x,int z,int seed,double now)
    {
        if(blackoutAfterEvent && now-globalStarted>=FlickerProfile.EventDuration)
        { blackoutAfterEvent=false; blackout=true; }
        if(blackout) return false;
        uint fixture=(uint)(x*73856093 ^ z*19349663 ^ seed);
        double eventAge=double.IsNegativeInfinity(chaseStarted) ? EventAge(now) :
            (now-chaseStarted)%FlickerProfile.EventDuration;
        if(eventAge>=0)
        {
            // Every panel has its own phase and switching rate during a roomwide
            // event. The event involves all panels without a shared blackout beat.
            double frequency=FlickerProfile.EventSpeedMin+
                Hash(fixture,0xA17E04u)*(FlickerProfile.EventSpeedMax-FlickerProfile.EventSpeedMin);
            double eventPhase=Hash(fixture,0xB20491u);
            uint beat=(uint)Math.Floor((eventAge*frequency)+eventPhase);
            float offChance=Math.Clamp(FlickerProfile.EventOffChance+
                (Hash(fixture,0xD3117Au)-0.5f)*0.22f,0.05f,0.95f);
            if(Hash(fixture^beat*0x9E3779B9u,0xF72A1Cu)<offChance) return false;
        }

        {
            double interval=FlickerProfile.MinorInterval;
            double shifted=now+Hash(fixture,0xD39B17u)*interval;
            int cycle=(int)Math.Floor(shifted/interval);
            double phase=shifted-cycle*interval;
            uint cycleKey=fixture^(uint)cycle*0x9E3779B9u;
            if(Hash(cycleKey,0x3B421Fu)<FlickerProfile.MinorChance)
            {
                double duration=FlickerProfile.MinorDuration*(0.67+0.66*Hash(cycleKey,0x747132u));
                if(phase<duration)
                {
                    int beat=(int)(phase*8.0);
                    if(beat==0 || Hash(cycleKey^(uint)beat,0x915772u)<0.55f) return false;
                }
            }
        }
        return true;
    }

    void Schedule(double now)
    {
        nextRandomEvent=now+FlickerProfile.EventInterval*(0.67+Random.Shared.NextDouble()*0.66);
    }

    static float Hash(uint value,uint salt)
    {
        unchecked
        {
            value^=salt;
            value^=value>>16;
            value*=0x7FEB352Du;
            value^=value>>15;
            value*=0x846CA68Bu;
            value^=value>>16;
            return (value>>8)*(1f/16777216f);
        }
    }
}
