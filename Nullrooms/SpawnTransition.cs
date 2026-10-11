namespace Beanrooms;

internal sealed class SpawnTransition
{
    double startedAt = double.NegativeInfinity;
    bool hit;

    internal void Begin(double now,bool monsterHit)
    {
        startedAt=now;
        hit=monsterHit;
    }

    internal (float Dark,float Flash) Sample(double now)
    {
        float age=(float)Math.Max(0,now-startedAt);
        if(!hit) return (1f-Smooth(0f,1.65f,age),0f);
        float flash=1f-Smooth(0.04f,0.26f,age);
        float dark=1f-Smooth(0.22f,2.05f,age);
        return (dark,flash);
    }

    static float Smooth(float start,float end,float value)
    {
        float t=Math.Clamp((value-start)/(end-start),0f,1f);
        return t*t*(3f-2f*t);
    }
}
