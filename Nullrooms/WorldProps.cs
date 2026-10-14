using System.Drawing;
using System.Numerics;

namespace Beanrooms;

// A few predictable objects keep pickup and physics part of normal play.
// The center of generated layouts is intentionally clear of walls.
internal static class WorldProps
{
    internal static void Place(PhysicsBodies physics,IReadOnlyList<RectangleF> walls)
    {
        Vector2 start=WorldLayout.FindClearSpawn(walls,new Vector2(WorldLayout.Center,WorldLayout.Center),.4f);
        Vector3 player=new(start.X,1.68f,start.Y);
        physics.SpawnAt(new Vector3(WorldLayout.Center+4f,.5f,WorldLayout.Center),false,walls,player,new Vector3(.82f,.69f,.22f));
        physics.SpawnAt(new Vector3(WorldLayout.Center-4f,.5f,WorldLayout.Center+3f),false,walls,player,new Vector3(.65f,.68f,.71f),PhysicsMaterial.Concrete);
    }
}
