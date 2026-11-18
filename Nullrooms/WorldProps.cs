using System.Drawing;
using System.Numerics;

namespace Beanrooms;

// A few predictable objects keep pickup and physics part of normal play.
// The center of generated layouts is intentionally clear of walls.
internal static class WorldProps
{
    internal static void Place(PhysicsBodies physics,IReadOnlyList<RectangleF> walls)
    {
        Vector2 start=WorldLayout.FindClearSpawn(walls,new Vector2(96f,96f),.4f);
        Vector3 player=new(start.X,1.68f,start.Y);
        physics.SpawnAt(new Vector3(100f,.5f,96f),false,walls,player,new Vector3(.82f,.69f,.22f));
        physics.SpawnAt(new Vector3(92f,.5f,99f),false,walls,player,new Vector3(.65f,.68f,.71f),PhysicsMaterial.Concrete);
    }
}
