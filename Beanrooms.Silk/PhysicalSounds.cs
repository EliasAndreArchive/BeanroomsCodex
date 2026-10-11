using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal readonly record struct FootstepSource(int Id,Vector3 Position,float Speed,float Sprint,float Crouch);
internal readonly record struct CubeSoundSource(int Id,Vector3 Position,Vector3 Velocity,float HalfSize,
    bool Grounded,bool Held,Quaternion Rotation,Vector3 AngularVelocity,float SlipSpeed,
    PhysicsMaterial Material=PhysicsMaterial.Wood,Vector3 LocalHalfExtents=default);

internal sealed class PhysicalSounds : IDisposable
{
    // Per-vertex contact state lets a cube landing on one corner, edge, or rolling
    // tick produce a hit for every corner that newly touches a surface.
    const float VertexFloorTolerance=0.045f;
    const float VertexSurfaceTolerance=0.05f;
    const float VertexImpactSpeed=0.85f;
    const float VertexCooldown=0.14f;
    const float Ceiling=3.6f;
    sealed class VertexState
    {
        internal readonly bool[] Touching=new bool[8];
        internal readonly float[] Cooldown=new float[8];
    }
    struct Contact
    {
        internal bool Touching;
        internal float Impact;
        internal float Slip;
    }

    readonly AudioClip[] stepClips,impactClips;
    readonly AudioVoice[][] stepVoices,impactVoices;
    readonly LoopingSound[] scrapes;
    readonly Dictionary<PhysicsMaterial,AudioClip[]> materialImpactClips=[];
    readonly Dictionary<PhysicsMaterial,AudioVoice[][]> materialImpacts=[];
    readonly Dictionary<PhysicsMaterial,LoopingSound[]> materialScrapes=[];
    readonly Dictionary<PhysicsMaterial,int> nextMaterialImpact=[];
    readonly Dictionary<int,float> vertexSlip=new();
    readonly Dictionary<int,float> stepTravel=new();
    readonly Dictionary<int,float> nextStride=new();
    readonly Dictionary<int,int> lastStepClip=new();
    readonly Dictionary<int,(Vector3 Position,float VerticalSpeed,bool Grounded)> previousCubes=new();
    readonly Dictionary<int,float> impactCooldown=new();
    readonly Dictionary<int,VertexState> vertexStates=new();
    int nextStepVoice;
    internal int ImpactPlayCount { get; private set; }
    internal int VertexHitCount { get; private set; }

    internal PhysicalSounds(AudioSystem system)
    {
        stepClips=Enumerable.Range(0,5).Select(i=>system.Load($"footstep_concrete_{i:000}.wav")).ToArray();
        stepVoices=stepClips.Select(clip=>new[]{new AudioVoice(clip),new AudioVoice(clip)}).ToArray();
        foreach(PhysicsMaterial material in Enum.GetValues<PhysicsMaterial>())
        {
            AudioClip[] clips=Enumerable.Range(0,2).Select(i=>system.Load($"impact_{material.ToString().ToLowerInvariant()}_{i:000}.wav")).ToArray();
            materialImpactClips[material]=clips;
            materialImpacts[material]=clips.Select(clip=>new[]{new AudioVoice(clip),new AudioVoice(clip)}).ToArray();
            materialScrapes[material]=Enumerable.Range(0,4).Select(_=>new LoopingSound(system,$"slide_{material.ToString().ToLowerInvariant()}.wav")).ToArray();
        }
        impactClips=materialImpactClips[PhysicsMaterial.Wood];
        impactVoices=materialImpacts[PhysicsMaterial.Wood];
        scrapes=materialScrapes[PhysicsMaterial.Wood];
    }

    static (float Gain,float Pan) Spatial(Vector3 listener,Vector3 source,Vector2 right,float falloff=100f)
    {
        Vector3 offset=source-listener;
        float distance=offset.Length();
        float gain=1f/(1f+distance*distance/falloff);
        float pan=distance>0.1f?Math.Clamp(Vector2.Dot(new Vector2(offset.X,offset.Z),right)/distance*0.85f,-0.85f,0.85f):0f;
        return (gain,pan);
    }

    static Quaternion Normalize(Quaternion rotation)
    {
        float lengthSquared=rotation.LengthSquared();
        if(lengthSquared<0.5f) return Quaternion.Identity;
        return MathF.Abs(lengthSquared-1f)>0.001f?Quaternion.Normalize(rotation):rotation;
    }

    // A tipping cube can move its centre sideways while the edge on the floor is stationary.
    // Scraping follows the velocity of the lowest contact points, not the centre of mass.
    // Online cubes carry the server-measured value in CubeSoundSource.SlipSpeed.
    static Vector3 LocalExtents(CubeSoundSource cube)
        => cube.LocalHalfExtents.LengthSquared()>0.000001f?cube.LocalHalfExtents:new Vector3(cube.HalfSize);
    internal static float FloorSlipSpeed(CubeSoundSource cube)
        => PhysicsBodies.ContactSlipSpeed(cube.Velocity,cube.AngularVelocity,cube.Rotation,LocalExtents(cube));

    internal void Update(Vector3 listener,float yaw,FootstepSource local,IReadOnlyList<FootstepSource> remote,
        IReadOnlyList<CubeSoundSource> cubes,IReadOnlyList<CubeImpactEvent> impacts,float dt,
        Func<int,VercidiumAcoustics.Filter>? acousticFilter=null,Func<int,int>? reverbGroup=null,
        IReadOnlyList<RectangleF>? walls=null)
    {
        VercidiumAcoustics.Filter FilterFor(int id)=>acousticFilter?.Invoke(id)??VercidiumAcoustics.Filter.Clear;
        int GroupFor(int id)=>reverbGroup?.Invoke(id)??-1;
        Vector2 right=new(MathF.Cos(yaw),-MathF.Sin(yaw));
        HashSet<int> active=[];
        Step(local,listener,right,dt,active,FilterFor,GroupFor);
        foreach(FootstepSource player in remote) Step(player,listener,right,dt,active,FilterFor,GroupFor);
        foreach(int id in stepTravel.Keys.Where(id=>!active.Contains(id)).ToArray())
        { stepTravel.Remove(id);nextStride.Remove(id);lastStepClip.Remove(id); }

        HashSet<int> heldCubes=cubes.Where(cube=>cube.Held).Select(cube=>cube.Id).ToHashSet();
        List<CubeImpactEvent> audibleImpacts=impacts.Where(impact=>!heldCubes.Contains(impact.CubeId)).ToList();
        HashSet<int> officialImpactIds=audibleImpacts.Select(impact=>impact.CubeId).ToHashSet();
        foreach(int id in impactCooldown.Keys.ToArray())
        {
            float remaining=impactCooldown[id]-Math.Clamp(dt,0f,0.05f);
            if(remaining<=0f) impactCooldown.Remove(id);
            else impactCooldown[id]=remaining;
        }
        foreach(int id in officialImpactIds) impactCooldown[id]=0.25f;
        HashSet<int> seenCubes=[];
        foreach(CubeSoundSource cube in cubes)
        {
            seenCubes.Add(cube.Id);
            bool grounded=cube.Grounded || cube.Position.Y-PhysicsBodies.RotatedExtents(LocalExtents(cube),Normalize(cube.Rotation)).Y<0.065f;
            if(previousCubes.TryGetValue(cube.Id,out var previous))
            {
                float measuredSpeed=Math.Clamp((cube.Position.Y-previous.Position.Y)/MathF.Max(dt,0.005f),-20f,20f);
                if(!cube.Held && grounded && !previous.Grounded && previous.VerticalSpeed < -0.65f &&
                   !impactCooldown.ContainsKey(cube.Id))
                {
                    audibleImpacts.Add(new CubeImpactEvent { CubeId=cube.Id,X=cube.Position.X,Y=cube.Position.Y,Z=cube.Position.Z,
                        Strength=Math.Clamp(-previous.VerticalSpeed/5f,0.25f,1f),Material=cube.Material });
                    impactCooldown[cube.Id]=0.25f;
                }
                previousCubes[cube.Id]=(cube.Position,measuredSpeed,grounded);
            }
            else previousCubes[cube.Id]=(cube.Position,cube.Velocity.Y,grounded);
        }
        foreach(int id in previousCubes.Keys.Where(id=>!seenCubes.Contains(id)).ToArray()) previousCubes.Remove(id);

        foreach(CubeImpactEvent impact in audibleImpacts)
            PlayImpact(impact.CubeId,new Vector3(impact.X,impact.Y,impact.Z),impact.Strength,impact.Material,listener,right,FilterFor,GroupFor);

        UpdateVertexHits(cubes,listener,right,walls??[],dt,FilterFor,GroupFor);

        // A held cube only scrapes while a corner is actually pressed against a
        // surface; its floor-contact slip is measured from the touching vertices
        // so carrying it through the air stays silent.
        var sliding=cubes.Where(cube=>cube.Held||cube.Grounded)
            .Select(cube=>
            {
                float speed=cube.Held?vertexSlip.GetValueOrDefault(cube.Id):cube.SlipSpeed;
                var (gain,pan)=Spatial(listener,cube.Position,right);
                VercidiumAcoustics.Filter filter=FilterFor(AcousticSourceIds.Cube(cube.Id));
                return (cube.Material,Gain:Math.Clamp((speed-0.12f)/2.2f,0f,1f)*gain*0.8f*filter.Gain,
                    Pan:pan,HighRatio:filter.HighRatio,Group:GroupFor(AcousticSourceIds.Cube(cube.Id)));
            })
            .Where(sound=>sound.Gain>0.012f)
            .OrderByDescending(sound=>sound.Gain).Take(8).ToArray();
        foreach(PhysicsMaterial material in Enum.GetValues<PhysicsMaterial>())
        {
            LoopingSound[] loops=materialScrapes[material];
            var sounds=sliding.Where(sound=>sound.Material==material).Take(loops.Length).ToArray();
            for(int i=0;i<loops.Length;i++)
            {
                loops[i].SetHighFrequencyGain(i<sounds.Length?sounds[i].HighRatio:1f);
                loops[i].SetReverbGroup(i<sounds.Length?sounds[i].Group:-1);
                loops[i].SetTarget(i<sounds.Length?sounds[i].Gain:0f,i<sounds.Length?sounds[i].Pan:0f);
                loops[i].Update(dt);
            }
        }
    }

    bool PlayImpact(int cubeId,Vector3 position,float strength,PhysicsMaterial material,Vector3 listener,Vector2 right,
        Func<int,VercidiumAcoustics.Filter> filterFor,Func<int,int> groupFor)
    {
        var (gain,pan)=Spatial(listener,position,right,400f);
        VercidiumAcoustics.Filter filter=filterFor(AcousticSourceIds.Cube(cubeId));
        gain*=filter.Gain;
        if(gain<0.02f) return false;
        AudioVoice[][] voices=materialImpacts[material];
        int index=nextMaterialImpact.GetValueOrDefault(material);
        nextMaterialImpact[material]=index+1;
        AudioVoice voice=voices[index%voices.Length][(index/voices.Length)%2];
        voice.SetPitch(0.88f+Random.Shared.NextSingle()*0.12f);
        voice.SetHighFrequencyGain(filter.HighRatio);
        voice.SetReverbGroup(groupFor(AcousticSourceIds.Cube(cubeId)));
        voice.Set(Math.Clamp((0.58f+0.42f*strength)*gain,0f,1f),pan);
        voice.Play();
        ImpactPlayCount++;
        return true;
    }

    void UpdateVertexHits(IReadOnlyList<CubeSoundSource> cubes,Vector3 listener,Vector2 right,
        IReadOnlyList<RectangleF> walls,float dt,Func<int,VercidiumAcoustics.Filter> filterFor,Func<int,int> groupFor)
    {
        float step=Math.Clamp(dt,0f,0.05f);
        HashSet<int> seen=[];
        vertexSlip.Clear();
        foreach(CubeSoundSource cube in cubes)
        {
            seen.Add(cube.Id);
            if(!vertexStates.TryGetValue(cube.Id,out VertexState? state)) { state=new VertexState();vertexStates[cube.Id]=state; }
            Quaternion rotation=Normalize(cube.Rotation);
            Vector3 half=LocalExtents(cube);
            float slip=0f;
            for(int index=0;index<8;index++)
            {
                state.Cooldown[index]=MathF.Max(0f,state.Cooldown[index]-step);
                int bx=(index&1)==0?-1:1,by=(index&2)==0?-1:1,bz=(index&4)==0?-1:1;
                Vector3 radius=Vector3.Transform(new Vector3(bx*half.X,by*half.Y,bz*half.Z),rotation);
                Vector3 vertex=cube.Position+radius;
                Vector3 velocity=cube.Velocity+Vector3.Cross(cube.AngularVelocity,radius);
                Contact contact=VertexContact(cube,vertex,velocity,walls,cubes);
                if(contact.Touching)
                {
                    slip=MathF.Max(slip,contact.Slip);
                    if(!state.Touching[index] && state.Cooldown[index]<=0f &&
                       contact.Impact>=VertexImpactSpeed && !impactCooldown.ContainsKey(cube.Id) &&
                       PlayImpact(cube.Id,vertex,Math.Clamp(contact.Impact/6f,0.15f,1f),cube.Material,listener,right,filterFor,groupFor))
                    {
                        state.Cooldown[index]=VertexCooldown;
                        VertexHitCount++;
                    }
                }
                state.Touching[index]=contact.Touching;
            }
            if(slip>0f) vertexSlip[cube.Id]=slip;
        }
        foreach(int id in vertexStates.Keys.Where(id=>!seen.Contains(id)).ToArray()) vertexStates.Remove(id);
    }

    Contact VertexContact(CubeSoundSource cube,Vector3 vertex,Vector3 velocity,
        IReadOnlyList<RectangleF> walls,IReadOnlyList<CubeSoundSource> cubes)
    {
        Contact result=default;
        if(vertex.Y<=VertexFloorTolerance)
        {
            result.Touching=true;
            result.Impact=MathF.Max(result.Impact,MathF.Max(0f,-velocity.Y));
            result.Slip=MathF.Max(result.Slip,MathF.Sqrt(velocity.X*velocity.X+velocity.Z*velocity.Z));
        }
        for(int i=0;i<walls.Count;i++)
        {
            RectangleF wall=walls[i];
            if(vertex.X<wall.Left-VertexSurfaceTolerance || vertex.X>wall.Right+VertexSurfaceTolerance ||
               vertex.Z<wall.Top-VertexSurfaceTolerance || vertex.Z>wall.Bottom+VertexSurfaceTolerance ||
               vertex.Y<0f || vertex.Y>Ceiling) continue;
            Vector3 normal=WallNormal(vertex,wall);
            result.Touching=true;
            result.Impact=MathF.Max(result.Impact,MathF.Max(0f,-Vector3.Dot(velocity,normal)));
            result.Slip=MathF.Max(result.Slip,(velocity-normal*Vector3.Dot(velocity,normal)).Length());
        }
        for(int i=0;i<cubes.Count;i++)
        {
            CubeSoundSource other=cubes[i];
            if(other.Id==cube.Id) continue;
            float reach=LocalExtents(other).Length()*1.15f;
            if(Vector3.DistanceSquared(vertex,other.Position)>reach*reach) continue;
            if(!InsideOrientedBox(vertex,other,out Vector3 normal)) continue;
            Vector3 relative=velocity-other.Velocity;
            result.Touching=true;
            result.Impact=MathF.Max(result.Impact,MathF.Max(0f,-Vector3.Dot(relative,normal)));
            result.Slip=MathF.Max(result.Slip,(relative-normal*Vector3.Dot(relative,normal)).Length());
        }
        return result;
    }

    static Vector3 WallNormal(Vector3 vertex,RectangleF wall)
    {
        float left=vertex.X-(wall.Left-VertexSurfaceTolerance);
        float right=(wall.Right+VertexSurfaceTolerance)-vertex.X;
        float top=vertex.Z-(wall.Top-VertexSurfaceTolerance);
        float bottom=(wall.Bottom+VertexSurfaceTolerance)-vertex.Z;
        float nearest=MathF.Min(MathF.Min(left,right),MathF.Min(top,bottom));
        if(nearest==left) return -Vector3.UnitX;
        if(nearest==right) return Vector3.UnitX;
        return nearest==top?-Vector3.UnitZ:Vector3.UnitZ;
    }

    static bool InsideOrientedBox(Vector3 point,CubeSoundSource other,out Vector3 normal)
    {
        Quaternion rotation=Normalize(other.Rotation);
        Vector3 local=Vector3.Transform(point-other.Position,Quaternion.Inverse(rotation));
        Vector3 half=LocalExtents(other)+new Vector3(VertexSurfaceTolerance);
        if(MathF.Abs(local.X)>=half.X || MathF.Abs(local.Y)>=half.Y || MathF.Abs(local.Z)>=half.Z)
        { normal=Vector3.Zero;return false; }
        float dx=half.X-MathF.Abs(local.X),dy=half.Y-MathF.Abs(local.Y),dz=half.Z-MathF.Abs(local.Z);
        Vector3 localNormal;
        if(dx<=dy && dx<=dz) localNormal=new(MathF.Sign(local.X)==0f?1f:MathF.Sign(local.X),0f,0f);
        else if(dy<=dz) localNormal=new(0f,MathF.Sign(local.Y)==0f?1f:MathF.Sign(local.Y),0f);
        else localNormal=new(0f,0f,MathF.Sign(local.Z)==0f?1f:MathF.Sign(local.Z));
        normal=Vector3.Transform(localNormal,rotation);
        return true;
    }

    void Step(FootstepSource player,Vector3 listener,Vector2 right,float dt,HashSet<int> active,
        Func<int,VercidiumAcoustics.Filter> filterFor,Func<int,int> groupFor)
    {
        active.Add(player.Id);
        float speed=Math.Clamp(player.Speed,0f,10f);
        if(speed<0.28f) { stepTravel[player.Id]=0f;return; }
        float travel=stepTravel.GetValueOrDefault(player.Id)+speed*Math.Clamp(dt,0f,0.05f);
        float baseStride=0.7f+0.25f*Math.Clamp(player.Sprint,0f,1f);
        baseStride+=(0.55f-baseStride)*Math.Clamp(player.Crouch,0f,1f);
        float stride=nextStride.GetValueOrDefault(player.Id,baseStride);
        if(travel>=stride)
        {
            travel-=stride;
            nextStride[player.Id]=baseStride*(0.87f+Random.Shared.NextSingle()*0.26f);
            var (gain,pan)=Spatial(listener,player.Position-new Vector3(0f,1.4f,0f),right);
            VercidiumAcoustics.Filter filter=player.Id==0?VercidiumAcoustics.Filter.Clear:
                filterFor(AcousticSourceIds.Player(player.Id));
            gain*=filter.Gain;
            if(gain>0.025f)
            {
                int previous=lastStepClip.GetValueOrDefault(player.Id,-1);
                int clip=previous<0?Random.Shared.Next(stepVoices.Length):Random.Shared.Next(stepVoices.Length-1);
                if(previous>=0 && clip>=previous) clip++;
                lastStepClip[player.Id]=clip;
                AudioVoice voice=stepVoices[clip][nextStepVoice++%2];
                voice.SetPitch(0.91f+Random.Shared.NextSingle()*0.18f);
                voice.SetHighFrequencyGain(filter.HighRatio);
                voice.SetReverbGroup(player.Id==0?-1:groupFor(AcousticSourceIds.Player(player.Id)));
                float variation=0.84f+Random.Shared.NextSingle()*0.16f;
                voice.Set(Math.Clamp((0.78f+0.22f*Math.Clamp(player.Sprint,0f,1f))*(1f-0.23f*Math.Clamp(player.Crouch,0f,1f))*gain*variation,0f,1f),pan);
                voice.Play();
            }
        }
        stepTravel[player.Id]=travel;
    }

    public void Dispose()
    {
        foreach(LoopingSound scrape in materialScrapes.Values.SelectMany(loops=>loops)) scrape.Dispose();
        foreach(AudioVoice voice in stepVoices.SelectMany(voices=>voices)) voice.Dispose();
        foreach(AudioVoice voice in materialImpacts.Values.SelectMany(groups=>groups).SelectMany(voices=>voices)) voice.Dispose();
        foreach(AudioClip clip in stepClips.Concat(materialImpactClips.Values.SelectMany(clips=>clips))) clip.Dispose();
    }
}
