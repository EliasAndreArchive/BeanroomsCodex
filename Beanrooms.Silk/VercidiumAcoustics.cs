using System.Drawing;
using System.Numerics;
using vaudio;
using AudioVector=vaudio.Vector;

namespace Beanrooms;

internal readonly record struct AcousticSource(int Id,Vector3 Position);

internal static class AcousticSourceIds
{
    internal const int Monster=1;
    internal static int Lamp(Vector2 position) => 1000+(int)(position.X/8f)+
        (WorldLayout.GridSize/2+1)*(int)(position.Y/8f);
    internal static int Player(int id) => 10000+id;
    internal static int Cube(int id) => 100000+id;
}

// Vercidium traces the world and supplies source occlusion and room reverb to OpenAL.
internal sealed class VercidiumAcoustics : IDisposable
{
    internal const int MaximumReverbGroups=4;
    internal readonly record struct ReverbGroup(RoomReverbSettings Settings,float Gain);
    internal readonly record struct Filter(float Gain,float HighRatio)
    {
        internal static Filter Clear => new(1f,1f);
    }

    sealed class SourceSlot
    {
        internal readonly Emitter Emitter=new() { ReverbEnergyCap=0.2f,
            EchogramGranularity=100,AffectsGroupedEAX=false };
        internal int Id;
    }

    readonly World world=new(null,null);
    readonly Emitter listener=new() { Name="listener",HasRelativeReverb=true,
        ReverbRayCount=32,ReverbBounceCount=32,ReverbEnergyCap=0.2f,
        OcclusionRayCount=256,OcclusionBounceCount=4,
        PermeationRayCount=64,PermeationBounceCount=2,AffectsGroupedEAX=false };
    readonly SourceSlot[] slots=Enumerable.Range(0,24).Select(_=>new SourceSlot()).ToArray();
    readonly Dictionary<int,Filter> filters=new();
    readonly Dictionary<int,int> reverbGroups=new();
    readonly List<ReverbGroup> groups=[];
    readonly int groupLimit;
    internal RoomReverbSettings Room { get; private set; }=RoomReverbSettings.Backrooms;
    internal IReadOnlyList<ReverbGroup> Groups => groups;
    internal bool HasRaytracedRoom => listener.EAX!=null;

    internal VercidiumAcoustics(WorldLayoutData layout,int groupLimit=MaximumReverbGroups)
        :this(layout.Walls,groupLimit,layout) { }

    internal VercidiumAcoustics(IReadOnlyList<RectangleF> walls,int groupLimit=MaximumReverbGroups,
        WorldLayoutData? layout=null)
    {
        this.groupLimit=Math.Clamp(groupLimit,1,MaximumReverbGroups);
        world.Position=new AudioVector(0,-5,0);
        world.Size=new AudioVector(WorldLayout.WorldSize,13,WorldLayout.WorldSize);
        world.MaximumConcurrencyLevel=2;
        world.MaximumGroupedEAXCount=this.groupLimit;
        for(int i=0;i<walls.Count;i++)
        {
            RectangleF wall=walls[i];
            float height=layout==null?WorldLayout.NormalCeiling:layout.WallHeights[i];
            AddBox((wall.Left+wall.Right)*0.5f,height*0.5f,(wall.Top+wall.Bottom)*0.5f,
                MathF.Max(0.05f,wall.Width),height,MathF.Max(0.05f,wall.Height),MaterialType.Gyprock);
        }
        if(layout==null)
        {
            AddBox(WorldLayout.Center,-0.05f,WorldLayout.Center,WorldLayout.WorldSize,0.1f,WorldLayout.WorldSize,MaterialType.Concrete);
            AddBox(WorldLayout.Center,WorldLayout.NormalCeiling+0.05f,WorldLayout.Center,
                WorldLayout.WorldSize,0.1f,WorldLayout.WorldSize,MaterialType.Gyprock);
        }
        else
        {
            foreach(RectangleF floor in WorldLayout.FloorSlabs(layout))
                AddBox(floor.Left+floor.Width*0.5f,-0.05f,floor.Top+floor.Height*0.5f,
                    floor.Width,0.1f,floor.Height,MaterialType.Concrete);
            AddBox(WorldLayout.Center,-5.05f,WorldLayout.Center,WorldLayout.WorldSize,0.1f,WorldLayout.WorldSize,MaterialType.Concrete);
            for(int z=0;z<WorldLayout.GridSize;z++)
            for(int x=0;x<WorldLayout.GridSize;)
            {
                float height=layout.Heights[(z*WorldLayout.GridSize+x)*4];
                int start=x++;
                while(x<WorldLayout.GridSize&&layout.Heights[(z*WorldLayout.GridSize+x)*4]==height) x++;
                AddBox((start+x)*2f,height+0.05f,z*4f+2f,(x-start)*4f,0.1f,4f,MaterialType.Gyprock);
            }
        }
        world.AddEmitter(listener);
        foreach(SourceSlot slot in slots)
        {
            world.AddEmitter(slot.Emitter);
            listener.AddTarget(slot.Emitter);
        }
    }

    void AddBox(float x,float y,float z,float width,float height,float length,MaterialType material)
    {
        world.AddPrimitive(new PrismPrimitive { material=material,size=new AudioVector(width,height,length),
            transform=Matrix.CreateTranslation(x,y,z) });
    }

    static Filter Gain(Emitter listener,Emitter source)
    {
        if(!listener.HasRaytracedTarget(source)) return Filter.Clear;
        LowPassFilter filter=listener.GetTargetFilter(source);
        float low=Math.Clamp(filter.GainLF,0.08f,1f);
        float high=Math.Clamp(filter.GainHF,0f,1f);
        return new Filter(low,Math.Clamp(high/low,0f,1f));
    }

    static RoomReverbSettings Settings(EAXReverb eax) => new(
        Math.Clamp(eax.Density,0f,1f),Math.Clamp(eax.Diffusion,0f,1f),
        Math.Clamp(eax.Gain,0f,1f),Math.Clamp(eax.GainHF,0f,1f),
        Math.Clamp(eax.DecayTime,0.1f,20f),Math.Clamp(eax.DecayHFRatio,0.1f,2f),
        Math.Clamp(eax.ReflectionsGain,0f,3.16f),Math.Clamp(eax.ReflectionsDelay,0f,0.3f),
        Math.Clamp(eax.LateReverbGain,0f,10f),Math.Clamp(eax.LateReverbDelay,0f,0.1f),
        Math.Clamp(eax.GainLF,0f,1f),Math.Clamp(eax.DecayLFRatio,0.1f,2f));

    internal void Update(Vector3 ear,IReadOnlyList<AcousticSource> sources)
    {
        listener.Position=new AudioVector(ear);
        bool[] used=new bool[slots.Length];
        HashSet<int> newlyAssigned=[];
        filters.Clear();
        reverbGroups.Clear();
        foreach(AcousticSource source in sources)
        {
            int index=-1;
            for(int i=0;i<slots.Length;i++)
                if(!used[i] && slots[i].Id==source.Id) { index=i;break; }
            if(index<0)
                for(int i=0;i<slots.Length;i++)
                    if(!used[i] && slots[i].Id==0) { index=i;break; }
            if(index<0)
                for(int i=0;i<slots.Length;i++)
                    if(!used[i]) { index=i;break; }
            if(index<0) break;
            SourceSlot slot=slots[index];
            used[index]=true;
            bool changed=slot.Id!=source.Id;
            if(changed) newlyAssigned.Add(source.Id);
            slot.Id=source.Id;
            if(slot.Emitter.ReverbRayCount==0)
            {
                slot.Emitter.ReverbRayCount=12;
                slot.Emitter.ReverbBounceCount=24;
            }
            slot.Emitter.AffectsGroupedEAX=true;
            slot.Emitter.Position=new AudioVector(source.Position);
        }
        for(int i=0;i<slots.Length;i++)
            if(!used[i])
            {
                slots[i].Id=0;
                slots[i].Emitter.AffectsGroupedEAX=false;
                slots[i].Emitter.ReverbRayCount=0;
                slots[i].Emitter.ReverbBounceCount=0;
                slots[i].Emitter.Position=new AudioVector(ear);
            }
        world.Update();
        for(int i=0;i<slots.Length;i++)
        {
            if(!used[i]) continue;
            SourceSlot slot=slots[i];
            filters[slot.Id]=newlyAssigned.Contains(slot.Id)?Filter.Clear:Gain(listener,slot.Emitter);
            if(!newlyAssigned.Contains(slot.Id) && listener.HasRaytracedTarget(slot.Emitter))
                reverbGroups[slot.Id]=slot.Emitter.GroupedEAXIndex;
        }
        EAXReverb? eax=listener.EAX;
        if(eax!=null) Room=Settings(eax);
        groups.Clear();
        foreach(EAXReverb grouped in world.GroupedEAX.Take(groupLimit))
        {
            float gain=grouped.RelativeGains.TryGetValue(listener,out float relative)?relative:1f;
            groups.Add(new ReverbGroup(Settings(grouped),Math.Clamp(gain,0f,1f)));
        }
    }

    internal Filter FilterFor(int id) => filters.GetValueOrDefault(id,Filter.Clear);
    internal int GroupFor(int id)
    {
        int index=reverbGroups.GetValueOrDefault(id,-1);
        return index>=0 && index<groups.Count?index:-1;
    }
    internal bool HasSourceResult(int id) => slots.Any(slot=>slot.Id==id && listener.HasRaytracedTarget(slot.Emitter));

    public void Dispose() => world.Dispose();
}
