using System.Drawing;
using System.Globalization;
using System.Numerics;

namespace Beanrooms;

internal readonly record struct CubeSpawnSpec(bool Emissive,float Size,Vector3 Color,int Amount);

internal static class CubeSpawner
{
    const int MaximumObjects=64;
    static readonly Dictionary<string,Vector3> Colors=new(StringComparer.OrdinalIgnoreCase)
    {
        ["WHITE"]=new(.92f,.92f,.92f),
        ["RED"]=new(.95f,.18f,.12f),
        ["GREEN"]=new(.16f,.82f,.26f),
        ["BLUE"]=new(.18f,.40f,1f),
        ["YELLOW"]=new(.95f,.85f,.20f),
        ["ORANGE"]=new(1f,.36f,.08f),
        ["PURPLE"]=new(.70f,.22f,.95f),
        ["CYAN"]=new(.12f,.85f,.92f),
        ["AMBER"]=new(.82f,.69f,.22f)
    };

    internal static bool TryParse(string command,out CubeSpawnSpec spec,out string error)
    {
        spec=default;
        string[] parts=command.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        string kind=parts.Length>0?parts[0].ToUpperInvariant():"";
        if(parts.Length!=4 || kind is not ("CUBE" or "GLOWCUBE"))
        { error="USE CUBE SIZE COLOR AMOUNT";return false; }
        if(!float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float size) ||
           !float.IsFinite(size) || size<0.25f || size>2.5f)
        { error="SIZE MUST BE 0.25 TO 2.5";return false; }
        if(!Colors.TryGetValue(parts[2],out Vector3 color))
        { error="UNKNOWN COLOR  /  TYPE HELP";return false; }
        if(!int.TryParse(parts[3],out int amount) || amount<1 || amount>8)
        { error="AMOUNT MUST BE 1 TO 8";return false; }
        spec=new(kind=="GLOWCUBE",size,color,amount);
        error="";
        return true;
    }

    internal static int Spawn(PhysicsBodies physics,IReadOnlyList<RectangleF> walls,
        Vector3 eye,Vector3 look,CubeSpawnSpec spec)
    {
        Vector3 forward=new(look.X,0,look.Z);
        if(forward.LengthSquared()<0.0001f) forward=Vector3.UnitZ;
        forward=Vector3.Normalize(forward);
        Vector3 right=new(forward.Z,0,-forward.X);
        float spacing=spec.Size+0.2f;
        int columns=Math.Min(spec.Amount,3),spawned=0;
        for(int i=0;i<spec.Amount && physics.Bodies.Count<MaximumObjects;i++)
        {
            int column=i%columns,row=i/columns;
            Vector3 position=eye+forward*(2.4f+row*spacing)+
                right*((column-(columns-1)*0.5f)*spacing);
            position.Y=spec.Size*0.5f+0.1f;
            if(physics.SpawnAt(position,spec.Emissive,walls,eye,spec.Color,
                PhysicsMaterial.Wood,spec.Size)) spawned++;
        }
        return spawned;
    }
}
