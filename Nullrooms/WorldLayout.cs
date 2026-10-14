using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal readonly record struct RaisedRoom(int X,int Z,int Width,int Depth,float Ceiling);
internal sealed record WorldLayoutData(float[] Grid,List<RectangleF> Walls,float[] Heights,
    List<float> WallHeights,List<RaisedRoom> RaisedRooms,List<RectangleF> Pits);

internal static class WorldLayout
{
    internal const int GridSize = 64;
    internal const float CellSize = 4f;
    internal const float WorldSize = GridSize*CellSize;
    internal const float Center = WorldSize*0.5f;
    internal const float NormalCeiling = 3.4f;
    internal const float MediumCeiling = 4.9f;
    internal const float HighCeiling = 7.2f;
    internal const int PitPixelsPerMeter=2;

    internal static float CeilingAt(WorldLayoutData layout,float x,float z)
    {
        int cx=Math.Clamp((int)MathF.Floor(x/CellSize),0,GridSize-1);
        int cz=Math.Clamp((int)MathF.Floor(z/CellSize),0,GridSize-1);
        return layout.Heights[(cz*GridSize+cx)*4];
    }

    internal static bool PitAt(WorldLayoutData layout,float x,float z)
        => layout.Pits.Any(pit=>x>=pit.Left&&x<pit.Right&&z>=pit.Top&&z<pit.Bottom);

    internal static Vector2 FindClearSpawn(WorldLayoutData layout,Vector2 wanted,float radius)
    {
        Vector2 candidate=FindClearSpawn(layout.Walls,wanted,radius);
        if(!NearPit(layout,candidate,radius)) return candidate;
        for(int ring=1;ring<=100;ring++)
        for(int z=-ring;z<=ring;z++) for(int x=-ring;x<=ring;x++)
        {
            if(Math.Abs(x)!=ring&&Math.Abs(z)!=ring) continue;
            Vector2 point=candidate+new Vector2(x*0.5f,z*0.5f);
            if(point.X<5f||point.Y<5f||point.X>WorldSize-5f||point.Y>WorldSize-5f||
               NearPit(layout,point,radius)) continue;
            if(FindClearSpawn(layout.Walls,point,radius)==point) return point;
        }
        throw new InvalidOperationException("No solid spawn point found.");
    }

    internal static bool NearPit(WorldLayoutData layout,Vector2 point,float radius)
        => layout.Pits.Any(pit=>point.X>pit.Left-radius&&point.X<pit.Right+radius&&
            point.Y>pit.Top-radius&&point.Y<pit.Bottom+radius);

    internal static IReadOnlyList<RectangleF> FloorSlabs(WorldLayoutData layout)
    {
        var result=new List<RectangleF>();
        var active=new Dictionary<(int Left,int Right),(int Top,int Bottom)>();
        int extent=(int)WorldSize*PitPixelsPerMeter;
        for(int z=0;z<=extent;z++)
        {
            var next=new Dictionary<(int Left,int Right),(int Top,int Bottom)>();
            if(z<extent)
            for(int x=0;x<extent;)
            {
                if(PitAt(layout,(x+0.5f)/PitPixelsPerMeter,(z+0.5f)/PitPixelsPerMeter)) { x++;continue; }
                int left=x;
                while(x<extent&&!PitAt(layout,(x+0.5f)/PitPixelsPerMeter,
                    (z+0.5f)/PitPixelsPerMeter)) x++;
                var key=(left,x);
                next[key]=active.TryGetValue(key,out var run)?(run.Top,z+1):(z,z+1);
            }
            foreach(var entry in active)
                if(!next.ContainsKey(entry.Key))
                    result.Add(RectangleF.FromLTRB(entry.Key.Left/(float)PitPixelsPerMeter,
                        entry.Value.Top/(float)PitPixelsPerMeter,entry.Key.Right/(float)PitPixelsPerMeter,
                        entry.Value.Bottom/(float)PitPixelsPerMeter));
            active=next;
        }
        return result;
    }

    internal static Vector2 FindClearSpawn(IReadOnlyList<RectangleF> walls, Vector2 wanted, float radius)
    {
        bool Clear(Vector2 point)
        {
            if(point.X<CellSize+radius || point.Y<CellSize+radius ||
               point.X>WorldSize-CellSize-radius || point.Y>WorldSize-CellSize-radius) return false;
            foreach(RectangleF wall in walls)
                if(point.X>wall.Left-radius && point.X<wall.Right+radius &&
                   point.Y>wall.Top-radius && point.Y<wall.Bottom+radius) return false;
            return true;
        }
        if(Clear(wanted)) return wanted;
        for(int ring=1;ring<=40;ring++)
            for(int z=-ring;z<=ring;z++) for(int x=-ring;x<=ring;x++)
            {
                if(Math.Abs(x)!=ring && Math.Abs(z)!=ring) continue;
                Vector2 candidate=wanted+new Vector2(x*0.5f,z*0.5f);
                if(Clear(candidate)) return candidate;
            }
        throw new InvalidOperationException("No clear spawn point found in the generated world.");
    }

    internal static Vector2 FindSafeRespawn(IReadOnlyList<RectangleF> walls,Vector2 threat,float radius,Random random)
    {
        Vector2 best=FindClearSpawn(walls,new Vector2(Center,Center),radius);
        float bestDistance=Vector2.DistanceSquared(best,threat);
        for(int attempt=0;attempt<48;attempt++)
        {
            Vector2 wanted=new(8f+random.NextSingle()*(WorldSize-16f),8f+random.NextSingle()*(WorldSize-16f));
            Vector2 candidate=FindClearSpawn(walls,wanted,radius);
            float distance=Vector2.DistanceSquared(candidate,threat);
            if(distance>bestDistance) { best=candidate;bestDistance=distance; }
            if(distance>=25f*25f) return candidate;
        }
        return best;
    }

    internal static Vector2 FindSafeRespawn(WorldLayoutData layout,Vector2 threat,float radius,Random random)
    {
        Vector2 best=FindClearSpawn(layout,new Vector2(Center,Center),radius);
        float bestDistance=Vector2.DistanceSquared(best,threat);
        for(int attempt=0;attempt<48;attempt++)
        {
            Vector2 wanted=new(8f+random.NextSingle()*(WorldSize-16f),
                8f+random.NextSingle()*(WorldSize-16f));
            Vector2 candidate=FindClearSpawn(layout,wanted,radius);
            float distance=Vector2.DistanceSquared(candidate,threat);
            if(distance>bestDistance) { best=candidate;bestDistance=distance; }
            if(distance>=25f*25f) return candidate;
        }
        return best;
    }

    internal static WorldLayoutData Generate(int seed)
    {
        var grid = new float[GridSize * GridSize * 4];
        Array.Fill(grid, -1f);
        var heights=new float[grid.Length];
        for(int cell=0;cell<GridSize*GridSize;cell++) heights[cell*4]=NormalCeiling;
        var walls = new List<RectangleF>();
        var wallHeights=new List<float>();
        var raisedRooms=new List<RaisedRoom>();
        var pits=new List<RectangleF>();
        var zone=new byte[GridSize*GridSize];
        var rng = new Random(seed);
        for(int tier=2;tier>=1;tier--)
        for(int room=0;room<(tier==2?3:7);room++)
        for(int attempt=0;attempt<200;attempt++)
        {
            int span=tier==2?8:6;
            int x=rng.Next(3,GridSize-span-3),z=rng.Next(3,GridSize-span-3);
            if(x<36&&x+span>28&&z<36&&z+span>28) continue;
            bool clear=true;
            for(int zz=z-2;zz<z+span+2&&clear;zz++) for(int xx=x-2;xx<x+span+2;xx++)
                if(zone[zz*GridSize+xx]!=0) { clear=false;break; }
            if(!clear) continue;
            float ceiling=tier==2?HighCeiling:MediumCeiling;
            raisedRooms.Add(new RaisedRoom(x,z,span,span,ceiling));
            for(int zz=z;zz<z+span;zz++) for(int xx=x;xx<x+span;xx++)
            {
                int cell=zz*GridSize+xx;
                zone[cell]=(byte)tier;
                heights[cell*4]=ceiling;
            }
            break;
        }
        for(int room=0;room<2;room++)
        for(int attempt=0;attempt<200;attempt++)
        {
            const int span=6;
            int x=rng.Next(3,GridSize-span-3),z=rng.Next(3,GridSize-span-3);
            if(x<36&&x+span>28&&z<36&&z+span>28) continue;
            bool clear=true;
            for(int zz=z-2;zz<z+span+2&&clear;zz++) for(int xx=x-2;xx<x+span+2;xx++)
                if(zone[zz*GridSize+xx]!=0) { clear=false;break; }
            if(!clear) continue;
            for(int zz=z;zz<z+span;zz++) for(int xx=x;xx<x+span;xx++) zone[zz*GridSize+xx]=3;
            float left=x*CellSize,top=z*CellSize;
            if(room==0)
                pits.Add(RectangleF.FromLTRB(left+6,top+7,left+18,top+17));
            else
                for(int row=0;row<6;row++) for(int col=0;col<6;col++)
                {
                    float px=left+2+col*3.5f,pz=top+2+row*3.5f;
                    pits.Add(RectangleF.FromLTRB(px,pz,px+2.5f,pz+2.5f));
                }
            break;
        }
        void Place(int x, int z, int sx, int sz, float padX, float padZ,float wallHeight=NormalCeiling)
        {
            float minX=x*CellSize+padX, minZ=z*CellSize+padZ;
            float maxX=(x+sx)*CellSize-padX, maxZ=(z+sz)*CellSize-padZ;
            walls.Add(RectangleF.FromLTRB(minX,minZ,maxX,maxZ));
            wallHeights.Add(wallHeight);
            for(int zz=z;zz<z+sz;zz++) for(int xx=x;xx<x+sx;xx++)
            {
                int o=(zz*GridSize+xx)*4;
                grid[o]=minX; grid[o+1]=minZ; grid[o+2]=maxX; grid[o+3]=maxZ;
                heights[o+1]=wallHeight;
            }
        }
        for(int z=0;z<GridSize;z++) for(int x=0;x<GridSize;x++)
            if(x==0||z==0||x==GridSize-1||z==GridSize-1) Place(x,z,1,1,0,0);
        for(int attempt=0;attempt<780;attempt++)
        {
            int x=rng.Next(2,GridSize-5),z=rng.Next(2,GridSize-5);
            int sx=rng.NextDouble()<0.45 ? rng.Next(2,5) : 1;
            int sz=sx>1 ? rng.Next(1,3) : rng.Next(1,4);
            if(x<36 && x+sx>28 && z<36 && z+sz>28) continue;
            bool free=true;
            for(int zz=z-1;zz<z+sz+1&&free;zz++) for(int xx=x-1;xx<x+sx+1;xx++)
                if(xx>=0&&zz>=0&&xx<GridSize&&zz<GridSize&&
                   (grid[(zz*GridSize+xx)*4]>=0 || zone[zz*GridSize+xx]!=0)) { free=false; break; }
            if(!free) continue;
            bool pillar=sx==1 && sz==1 && rng.NextDouble()<0.58;
            float padX=pillar ? (float)(0.9+rng.NextDouble()*0.55) : (float)(rng.NextDouble()*0.35);
            float padZ=pillar ? (float)(0.9+rng.NextDouble()*0.55) : (float)(rng.NextDouble()*0.35);
            Place(x,z,sx,sz,padX,padZ);
        }
        var primaryWalls=new bool[GridSize*GridSize];
        var narrowSections=new bool[GridSize*GridSize];
        for(int cell=0;cell<primaryWalls.Length;cell++) primaryWalls[cell]=grid[cell*4]>=0f;
        Span<int> sides=stackalloc int[4];
        for(int attempt=0;attempt<460;attempt++)
        {
            int x=rng.Next(2,GridSize-2),z=rng.Next(2,GridSize-2);
            int cell=z*GridSize+x;
            if(primaryWalls[cell]||narrowSections[cell]||zone[cell]!=0) continue;
            if(x>=29&&x<=34&&z>=29&&z<=34) continue;
            if(narrowSections[cell-1]||narrowSections[cell+1]||
                narrowSections[cell-GridSize]||narrowSections[cell+GridSize]) continue;
            int count=0;
            if(primaryWalls[cell-1]) sides[count++]=0;
            if(primaryWalls[cell+1]) sides[count++]=1;
            if(primaryWalls[cell-GridSize]) sides[count++]=2;
            if(primaryWalls[cell+GridSize]) sides[count++]=3;
            if(count==0) continue;
            int side=sides[rng.Next(count)];
            float reach=1.35f+(float)rng.NextDouble()*0.55f;
            float left=x*CellSize,top=z*CellSize;
            RectangleF neck=side switch
            {
                0=>RectangleF.FromLTRB(left-0.12f,top+0.10f,left+reach,top+CellSize-0.10f),
                1=>RectangleF.FromLTRB(left+CellSize-reach,top+0.10f,left+CellSize+0.12f,top+CellSize-0.10f),
                2=>RectangleF.FromLTRB(left+0.10f,top-0.12f,left+CellSize-0.10f,top+reach),
                _=>RectangleF.FromLTRB(left+0.10f,top+CellSize-reach,left+CellSize-0.10f,top+CellSize+0.12f)
            };
            walls.Add(neck);
            wallHeights.Add(NormalCeiling);
            int offset=cell*4;
            grid[offset]=neck.Left;grid[offset+1]=neck.Top;
            grid[offset+2]=neck.Right;grid[offset+3]=neck.Bottom;
            heights[offset+1]=NormalCeiling;
            narrowSections[cell]=true;
        }
        // The raised rooms stay mostly open. Their sparse walls vary in height,
        // leaving visible gaps below the higher ceiling beside full-height columns.
        foreach(RaisedRoom room in raisedRooms)
        {
            int pieces=room.Ceiling==HighCeiling?8:5;
            for(int attempt=0,placed=0;attempt<pieces*20&&placed<pieces;attempt++)
            {
                int sx=rng.NextDouble()<0.55?1:rng.Next(2,4);
                int sz=sx==1?rng.Next(1,3):1;
                int x=rng.Next(room.X+1,room.X+room.Width-sx);
                int z=rng.Next(room.Z+1,room.Z+room.Depth-sz);
                bool clear=true;
                for(int zz=z-1;zz<z+sz+1&&clear;zz++) for(int xx=x-1;xx<x+sx+1;xx++)
                    if(xx>=room.X&&xx<room.X+room.Width&&zz>=room.Z&&zz<room.Z+room.Depth&&
                       grid[(zz*GridSize+xx)*4]>=0) { clear=false;break; }
                if(!clear) continue;
                bool pillar=sx==1&&sz==1;
                float pad=pillar?0.85f:0.2f;
                float height=room.Ceiling==HighCeiling
                    ?(rng.NextDouble()<0.48?HighCeiling:rng.NextDouble()<0.5?4.5f:5.5f)
                    :(rng.NextDouble()<0.7?MediumCeiling:NormalCeiling);
                Place(x,z,sx,sz,pad,pad,height);
                placed++;
            }
        }
        return new WorldLayoutData(grid,walls,heights,wallHeights,raisedRooms,pits);
    }
}
