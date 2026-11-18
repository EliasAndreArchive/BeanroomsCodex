using System.Drawing;
using System.Numerics;

namespace Beanrooms;

// Runs on the server for multiplayer and on the client when playing alone.
internal sealed class MonsterBrain
{
    const int Size = 192;
    const float Cell = 1f;
    const float Radius = 0.48f;
    readonly bool[] staticBlocked = new bool[Size * Size];
    bool[] blocked = new bool[Size * Size];
    bool[] nextBlocked = new bool[Size * Size];
    readonly List<RectangleF> cubeFootprints = [];
    readonly List<RectangleF> walls;
    readonly Random random;
    readonly MonsterTuning tuning;
    readonly List<Vector2> route = [];
    readonly MonsterState state = new();
    Vector2? lastSeen;
    Vector2 goal;
    int waypoint;
    float routeTimer;
    float sightTimer;
    float obstacleTimer;
    bool routeDirty = true;
    bool wasEnabled = true;

    internal MonsterState State => state;

    internal MonsterBrain(int seed, List<RectangleF> walls, MonsterTuning? settings = null)
    {
        this.walls = walls;
        tuning = settings ?? new MonsterTuning();
        random = new Random(seed ^ 0x5EED57);
        foreach(RectangleF wall in walls)
        {
            int x0 = Math.Clamp((int)MathF.Floor((wall.Left - Radius) / Cell), 0, Size - 1);
            int x1 = Math.Clamp((int)MathF.Floor((wall.Right + Radius) / Cell), 0, Size - 1);
            int z0 = Math.Clamp((int)MathF.Floor((wall.Top - Radius) / Cell), 0, Size - 1);
            int z1 = Math.Clamp((int)MathF.Floor((wall.Bottom + Radius) / Cell), 0, Size - 1);
            for(int z=z0;z<=z1;z++) for(int x=x0;x<=x1;x++)
            {
                Vector2 center = Center(z*Size+x);
                if(center.X>wall.Left-Radius && center.X<wall.Right+Radius &&
                   center.Y>wall.Top-Radius && center.Y<wall.Bottom+Radius)
                    staticBlocked[z*Size+x]=true;
            }
        }
        Array.Copy(staticBlocked,blocked,blocked.Length);
        Vector2 clearSpawn=WorldLayout.FindClearSpawn(walls,new Vector2(108f,98f),Radius);
        int spawn = NearestFree(Index(clearSpawn.X,clearSpawn.Y));
        Vector2 start = WorldLayout.FindClearSpawn(walls,Center(spawn),Radius);
        state.X=start.X; state.Y=1.5f; state.Z=start.Y;
        state.Yaw=MathF.Atan2(96f-start.X,96f-start.Y);
        SelectRoamGoal();
        PublishDebug();
    }

    static int Index(float x,float z) => Math.Clamp((int)(z/Cell),0,Size-1)*Size + Math.Clamp((int)(x/Cell),0,Size-1);
    static Vector2 Center(int index) => new((index%Size+0.5f)*Cell,(index/Size+0.5f)*Cell);
    static float DistanceSquared(Vector2 a,Vector2 b) => Vector2.DistanceSquared(a,b);
    Vector2 Position => new(state.X,state.Z);

    int NearestFree(int desired)
    {
        if(!blocked[desired]) return desired;
        int x=desired%Size,z=desired/Size;
        for(int radius=1;radius<Size;radius++)
            for(int dz=-radius;dz<=radius;dz++) for(int dx=-radius;dx<=radius;dx++)
            {
                int xx=x+dx,zz=z+dz;
                if(xx>=0&&xx<Size&&zz>=0&&zz<Size&&!blocked[zz*Size+xx]) return zz*Size+xx;
            }
        return desired;
    }

    bool Free(float x,float z)
    {
        if(x<4.5f||z<4.5f||x>187.5f||z>187.5f) return false;
        foreach(RectangleF wall in walls)
            if(x>wall.Left-Radius&&x<wall.Right+Radius&&z>wall.Top-Radius&&z<wall.Bottom+Radius) return false;
        foreach(RectangleF cube in cubeFootprints)
            if(cube.Contains(x,z)) return false;
        return true;
    }

    void RefreshCubeObstacles(float dt,IReadOnlyList<RigidCube> cubes)
    {
        obstacleTimer-=dt;
        cubeFootprints.Clear();
        foreach(RigidCube cube in cubes)
        {
            Vector3 extents=PhysicsBodies.Extents(cube);
            if(cube.Position.Y-extents.Y>2.5f || cube.Position.Y+extents.Y<0.2f) continue;
            float minX=cube.Position.X-extents.X-Radius;
            float maxX=cube.Position.X+extents.X+Radius;
            float minZ=cube.Position.Z-extents.Z-Radius;
            float maxZ=cube.Position.Z+extents.Z+Radius;
            cubeFootprints.Add(RectangleF.FromLTRB(minX,minZ,maxX,maxZ));
        }
        if(obstacleTimer>0f) return;
        obstacleTimer=0.12f;
        Array.Copy(staticBlocked,nextBlocked,staticBlocked.Length);
        foreach(RectangleF cube in cubeFootprints)
        {
            int x0=Math.Clamp((int)MathF.Floor(cube.Left/Cell),0,Size-1);
            int x1=Math.Clamp((int)MathF.Floor(cube.Right/Cell),0,Size-1);
            int z0=Math.Clamp((int)MathF.Floor(cube.Top/Cell),0,Size-1);
            int z1=Math.Clamp((int)MathF.Floor(cube.Bottom/Cell),0,Size-1);
            for(int z=z0;z<=z1;z++) for(int x=x0;x<=x1;x++)
            {
                Vector2 center=Center(z*Size+x);
                if(cube.Contains(center.X,center.Y))
                    nextBlocked[z*Size+x]=true;
            }
        }
        if(!blocked.AsSpan().SequenceEqual(nextBlocked))
        {
            (blocked,nextBlocked)=(nextBlocked,blocked);
            routeTimer=0f;
        }
    }

    static bool SegmentHits(Vector2 a,Vector2 b,RectangleF wall)
    {
        Vector2 delta=b-a;
        float enter=0f,leave=1f;
        bool Slab(float origin,float direction,float low,float high)
        {
            if(MathF.Abs(direction)<0.00001f) return origin>=low && origin<=high;
            float t0=(low-origin)/direction,t1=(high-origin)/direction;
            if(t0>t1) (t0,t1)=(t1,t0);
            enter=MathF.Max(enter,t0);leave=MathF.Min(leave,t1);
            return enter<=leave;
        }
        return Slab(a.X,delta.X,wall.Left,wall.Right) && Slab(a.Y,delta.Y,wall.Top,wall.Bottom);
    }

    bool CanSee(Vector2 player)
    {
        Vector2 difference=player-Position;
        float length=difference.Length();
        if(length>tuning.SightRange||length<0.001f) return length<0.001f;
        Vector2 forward=new(MathF.Sin(state.Yaw),MathF.Cos(state.Yaw));
        float minimumFacing=MathF.Cos(tuning.SightAngle*MathF.PI/360f);
        if(Vector2.Dot(forward,difference/length)<minimumFacing) return false;
        foreach(RectangleF wall in walls)
            if(wall.Right>=MathF.Min(state.X,player.X)&&wall.Left<=MathF.Max(state.X,player.X)&&
               wall.Bottom>=MathF.Min(state.Z,player.Y)&&wall.Top<=MathF.Max(state.Z,player.Y)&&
               SegmentHits(Position,player,wall)) return false;
        return true;
    }

    void SelectRoamGoal()
    {
        Vector2 origin=Position;
        for(int i=0;i<28;i++)
        {
            float x=origin.X+(float)(random.NextDouble()*2-1)*tuning.RoamRadius;
            float z=origin.Y+(float)(random.NextDouble()*2-1)*tuning.RoamRadius;
            int cell=NearestFree(Index(x,z));
            Vector2 candidate=Center(cell);
            float minimumDistance=MathF.Max(4f,MathF.Min(30f,tuning.RoamRadius*tuning.RoamRadius*0.15f));
            if(DistanceSquared(candidate,origin)>minimumDistance && TryRoute(candidate)) { goal=candidate;return; }
        }
        goal=origin;
        route.Clear();
        routeDirty=true;
    }

    bool TryRoute(Vector2 destination)
    {
        int start=NearestFree(Index(state.X,state.Z));
        int finish=NearestFree(Index(destination.X,destination.Y));
        var cost=new float[blocked.Length];
        var parent=new int[blocked.Length];
        Array.Fill(cost,float.PositiveInfinity);
        Array.Fill(parent,-1);
        var open=new PriorityQueue<int,float>();
        cost[start]=0f;open.Enqueue(start,0f);
        ReadOnlySpan<int> dx=[1,-1,0,0,1,1,-1,-1];
        ReadOnlySpan<int> dz=[0,0,1,-1,1,-1,1,-1];
        while(open.TryDequeue(out int current,out float priority))
        {
            int cx=current%Size,cz=current/Size;
            float expected=cost[current]+MathF.Sqrt((finish%Size-cx)*(finish%Size-cx)+(finish/Size-cz)*(finish/Size-cz));
            if(priority>expected+0.001f) continue;
            if(current==finish) break;
            for(int step=0;step<8;step++)
            {
                int x=cx+dx[step],z=cz+dz[step];
                if(x<0||z<0||x>=Size||z>=Size) continue;
                int next=z*Size+x;
                if(blocked[next]) continue;
                if(step>=4&&(blocked[cz*Size+x]||blocked[z*Size+cx])) continue;
                float candidate=cost[current]+(step>=4?1.414214f:1f);
                if(candidate>=cost[next]) continue;
                cost[next]=candidate;parent[next]=current;
                float heuristic=MathF.Sqrt((finish%Size-x)*(finish%Size-x)+(finish/Size-z)*(finish/Size-z));
                open.Enqueue(next,candidate+heuristic);
            }
        }
        if(start!=finish && parent[finish]<0) return false;
        route.Clear();
        for(int current=finish;current!=start && current>=0;current=parent[current]) route.Add(Center(current));
        route.Reverse();waypoint=0;
        routeDirty=true;
        return true;
    }

    internal void Update(float dt, IReadOnlyList<PlayerState> players,IReadOnlyList<RigidCube> cubes)
    {
        dt=Math.Clamp(dt,0f,0.05f);
        if(!tuning.Enabled)
        {
            if(wasEnabled)
            {
                state.Mode=0;state.TargetId=0;lastSeen=null;
                route.Clear();routeDirty=true;PublishDebug();
            }
            wasEnabled=false;
            return;
        }
        RefreshCubeObstacles(dt,cubes);
        if(!wasEnabled) { wasEnabled=true;routeTimer=0f;SelectRoamGoal(); }
        sightTimer-=dt;
        if(sightTimer<=0f)
        {
            sightTimer=0.1f;
            Vector2? visible=null;
            float nearest=float.PositiveInfinity;
            foreach(PlayerState player in players)
            {
                Vector2 position=new(player.X,player.Z);
                float distance=DistanceSquared(Position,position);
                if(distance<nearest&&CanSee(position)) { visible=position;nearest=distance;state.TargetId=player.Id; }
            }
            if(visible.HasValue)
            {
                lastSeen=visible;
                state.Mode=2;
            }
            else if(state.Mode==2) state.Mode=1;
        }
        float arrivalDistance=tuning.SearchRadius*tuning.SearchRadius;
        if(state.Mode==1&&lastSeen.HasValue&&DistanceSquared(Position,lastSeen.Value)<arrivalDistance)
        {
            lastSeen=null;state.Mode=0;state.TargetId=0;SelectRoamGoal();
        }
        Vector2 destination=lastSeen??goal;
        if(state.Mode==0&&DistanceSquared(Position,goal)<arrivalDistance) { SelectRoamGoal();destination=goal; }
        routeTimer-=dt;
        if(routeTimer<=0f || route.Count==0)
        {
            routeTimer=state.Mode==2?0.45f:1.2f;
            TryRoute(destination);
        }
        while(waypoint<route.Count && DistanceSquared(Position,route[waypoint])<0.85f)
        { waypoint++;routeDirty=true; }
        Vector2 target=waypoint<route.Count?route[waypoint]:destination;
        Vector2 toTarget=target-Position;
        if(toTarget.LengthSquared()<0.02f) { PublishDebug();return; }
        float desiredYaw=MathF.Atan2(toTarget.X,toTarget.Y);
        float angle=MathF.Atan2(MathF.Sin(desiredYaw-state.Yaw),MathF.Cos(desiredYaw-state.Yaw));
        float turnDegrees=state.Mode==2?tuning.ChaseTurn:state.Mode==1?tuning.SearchTurn:tuning.RoamTurn;
        float turnSpeed=turnDegrees*MathF.PI/180f;
        state.Yaw+=Math.Clamp(angle,-turnSpeed*dt,turnSpeed*dt);
        float alignment=MathF.Max(0f,MathF.Cos(angle));
        float baseSpeed=state.Mode==2?tuning.ChaseSpeed:state.Mode==1?tuning.SearchSpeed:tuning.RoamSpeed;
        float speed=baseSpeed*(0.15f+0.85f*alignment);
        float stepX=MathF.Sin(state.Yaw)*speed*dt,stepZ=MathF.Cos(state.Yaw)*speed*dt;
        if(Free(state.X+stepX,state.Z+stepZ)) { state.X+=stepX;state.Z+=stepZ; }
        else if(Free(state.X+stepX,state.Z)) state.X+=stepX;
        else if(Free(state.X,state.Z+stepZ)) state.Z+=stepZ;
        else routeTimer=0f;
        PublishDebug();
    }

    void PublishDebug()
    {
        Vector2 destination=lastSeen??goal;
        state.GoalX=destination.X;state.GoalZ=destination.Y;
        state.HasLastSeen=lastSeen.HasValue;
        state.LastX=lastSeen?.X??0f;state.LastZ=lastSeen?.Y??0f;
        if(routeDirty)
        {
            state.Route=route.Skip(waypoint).Take(24)
                .Select(point=>new NavPoint { X=point.X,Z=point.Y }).ToArray();
            routeDirty=false;
        }
    }
}
