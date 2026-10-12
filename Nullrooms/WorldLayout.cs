using System.Drawing;
using System.Numerics;

namespace Beanrooms;

internal static class WorldLayout
{
    internal const int GridSize = 48;
    internal const float CellSize = 4f;

    internal static Vector2 FindClearSpawn(IReadOnlyList<RectangleF> walls, Vector2 wanted, float radius)
    {
        bool Clear(Vector2 point)
        {
            if(point.X<4f+radius || point.Y<4f+radius ||
               point.X>188f-radius || point.Y>188f-radius) return false;
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
        Vector2 best=FindClearSpawn(walls,new Vector2(96f,96f),radius);
        float bestDistance=Vector2.DistanceSquared(best,threat);
        for(int attempt=0;attempt<48;attempt++)
        {
            Vector2 wanted=new(8f+random.NextSingle()*176f,8f+random.NextSingle()*176f);
            Vector2 candidate=FindClearSpawn(walls,wanted,radius);
            float distance=Vector2.DistanceSquared(candidate,threat);
            if(distance>bestDistance) { best=candidate;bestDistance=distance; }
            if(distance>=25f*25f) return candidate;
        }
        return best;
    }

    internal static (float[] Grid, List<RectangleF> Walls) Generate(int seed)
    {
        var grid = new float[GridSize * GridSize * 4];
        Array.Fill(grid, -1f);
        var walls = new List<RectangleF>();
        var rng = new Random(seed);
        void Place(int x, int z, int sx, int sz, float padX, float padZ)
        {
            float minX=x*CellSize+padX, minZ=z*CellSize+padZ;
            float maxX=(x+sx)*CellSize-padX, maxZ=(z+sz)*CellSize-padZ;
            walls.Add(RectangleF.FromLTRB(minX,minZ,maxX,maxZ));
            for(int zz=z;zz<z+sz;zz++) for(int xx=x;xx<x+sx;xx++)
            {
                int o=(zz*GridSize+xx)*4;
                grid[o]=minX; grid[o+1]=minZ; grid[o+2]=maxX; grid[o+3]=maxZ;
            }
        }
        for(int z=0;z<GridSize;z++) for(int x=0;x<GridSize;x++)
            if(x==0||z==0||x==GridSize-1||z==GridSize-1) Place(x,z,1,1,0,0);
        for(int attempt=0;attempt<440;attempt++)
        {
            int x=rng.Next(2,GridSize-5),z=rng.Next(2,GridSize-5);
            int sx=rng.NextDouble()<0.45 ? rng.Next(2,5) : 1;
            int sz=sx>1 ? rng.Next(1,3) : rng.Next(1,4);
            if(x<26 && x+sx>22 && z<26 && z+sz>22) continue;
            bool free=true;
            for(int zz=z-1;zz<z+sz+1&&free;zz++) for(int xx=x-1;xx<x+sx+1;xx++)
                if(xx>=0&&zz>=0&&xx<GridSize&&zz<GridSize&&grid[(zz*GridSize+xx)*4]>=0) { free=false; break; }
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
        for(int attempt=0;attempt<260;attempt++)
        {
            int x=rng.Next(2,GridSize-2),z=rng.Next(2,GridSize-2);
            int cell=z*GridSize+x;
            if(primaryWalls[cell]||narrowSections[cell]) continue;
            if(x>=21&&x<=26&&z>=21&&z<=26) continue;
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
            int offset=cell*4;
            grid[offset]=neck.Left;grid[offset+1]=neck.Top;
            grid[offset+2]=neck.Right;grid[offset+3]=neck.Bottom;
            narrowSections[cell]=true;
        }
        return (grid,walls);
    }
}
