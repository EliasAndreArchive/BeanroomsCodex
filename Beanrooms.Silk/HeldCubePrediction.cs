using System.Drawing;
using System.Numerics;

namespace Beanrooms;

// Visual prediction only. The dedicated server remains the source of cube physics.
internal sealed class HeldCubePrediction
{
    const float Ceiling = 3.4f;
    const float RoomMin = 4f, RoomMax = 188f;
    Vector3 position, localGrabPoint;
    Quaternion rotation, carryOffset;
    float grabDistanceOffset, halfSize;

    internal int CubeId { get; private set; }
    internal bool Active => CubeId != 0;

    internal void Clear() => CubeId = 0;

    internal bool TryBegin(CubeState[] cubes, Vector3 eye, Vector3 forward,
        Quaternion cameraRotation, float pickupRange, float holdDistance,
        IReadOnlyList<RectangleF> walls)
    {
        Clear();
        float nearest = MathF.Min(pickupRange, PhysicsBodies.WallRay(eye, forward, walls, pickupRange));
        CubeState? candidate = null;
        foreach(CubeState cube in cubes)
        {
            Quaternion cubeRotation = new(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
            if(cubeRotation.LengthSquared()<0.5f) continue;
            Quaternion inverse = Quaternion.Inverse(cubeRotation);
            Vector3 center = new(cube.X,cube.Y,cube.Z);
            float hit = PhysicsBodies.RayBox(Vector3.Transform(eye-center,inverse),
                Vector3.Transform(forward,inverse),Vector3.Zero,new Vector3(cube.HalfSize));
            if(hit>=0f && hit<nearest) { nearest=hit;candidate=cube; }
        }
        if(candidate==null) return false;

        CubeId=candidate.Id;
        position=new Vector3(candidate.X,candidate.Y,candidate.Z);
        rotation=Quaternion.Normalize(new Quaternion(candidate.Qx,candidate.Qy,candidate.Qz,candidate.Qw));
        halfSize=candidate.HalfSize;
        localGrabPoint=Vector3.Transform(eye+forward*nearest-position,Quaternion.Inverse(rotation));
        carryOffset=Quaternion.Normalize(Quaternion.Inverse(cameraRotation)*rotation);
        grabDistanceOffset=nearest-holdDistance;
        return true;
    }

    internal bool Apply(float dt, Vector3 eye, Vector3 forward, Quaternion cameraRotation,
        float holdDistance, float smoothing, IReadOnlyList<RectangleF> walls,
        IReadOnlyList<CubeState> cubes, CubeState visible)
    {
        if(!Active || visible.Id!=CubeId) return false;
        float blend=1f-MathF.Exp(-MathF.Max(smoothing,1f)*Math.Clamp(dt,0f,0.05f));
        Quaternion wantedRotation=Quaternion.Normalize(cameraRotation*carryOffset);
        Quaternion nextRotation=Quaternion.Normalize(Quaternion.Slerp(rotation,wantedRotation,blend));
        Vector3 extents=Extents(halfSize,nextRotation);
        Vector3 wanted=eye+forward*MathF.Max(0f,holdDistance+grabDistanceOffset)
            -Vector3.Transform(localGrabPoint,nextRotation);
        wanted.X=Math.Clamp(wanted.X,RoomMin+extents.X,RoomMax-extents.X);
        wanted.Y=Math.Clamp(wanted.Y,extents.Y+0.005f,Ceiling-extents.Y-0.005f);
        wanted.Z=Math.Clamp(wanted.Z,RoomMin+extents.Z,RoomMax-extents.Z);
        Vector3 next=Vector3.Lerp(position,wanted,blend);
        // Resolve each horizontal axis independently so a held cube can slide along a wall.
        Vector3 candidate=new(next.X,next.Y,position.Z);
        if(!Blocked(candidate,extents,walls,cubes)) position.X=candidate.X;
        candidate=new(position.X,next.Y,next.Z);
        if(!Blocked(candidate,extents,walls,cubes)) position.Z=candidate.Z;
        candidate=new(position.X,next.Y,position.Z);
        if(!Blocked(candidate,extents,walls,cubes)) position.Y=next.Y;
        if(!Blocked(position,extents,walls,cubes)) rotation=nextRotation;

        visible.X=position.X;visible.Y=position.Y;visible.Z=position.Z;
        visible.Qx=rotation.X;visible.Qy=rotation.Y;visible.Qz=rotation.Z;visible.Qw=rotation.W;
        return true;
    }

    static Vector3 Extents(float half,Quaternion orientation)
    {
        Matrix4x4 m=Matrix4x4.CreateFromQuaternion(orientation);
        return new Vector3(
            half*(MathF.Abs(m.M11)+MathF.Abs(m.M21)+MathF.Abs(m.M31)),
            half*(MathF.Abs(m.M12)+MathF.Abs(m.M22)+MathF.Abs(m.M32)),
            half*(MathF.Abs(m.M13)+MathF.Abs(m.M23)+MathF.Abs(m.M33)));
    }

    bool Blocked(Vector3 center,Vector3 extent,IReadOnlyList<RectangleF> walls,
        IReadOnlyList<CubeState> cubes)
    {
        foreach(RectangleF wall in walls)
            if(center.X+extent.X>wall.Left && center.X-extent.X<wall.Right &&
               center.Z+extent.Z>wall.Top && center.Z-extent.Z<wall.Bottom) return true;
        foreach(CubeState other in cubes)
        {
            if(other.Id==CubeId) continue;
            Quaternion orientation=new(other.Qx,other.Qy,other.Qz,other.Qw);
            if(orientation.LengthSquared()<0.5f) continue;
            Vector3 otherExtent=Extents(other.HalfSize,Quaternion.Normalize(orientation));
            if(MathF.Abs(center.X-other.X)<extent.X+otherExtent.X &&
               MathF.Abs(center.Y-other.Y)<extent.Y+otherExtent.Y &&
               MathF.Abs(center.Z-other.Z)<extent.Z+otherExtent.Z) return true;
        }
        return false;
    }
}
