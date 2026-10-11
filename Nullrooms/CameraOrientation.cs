using System.Numerics;

namespace Beanrooms;

internal static class CameraOrientation
{
    internal static Vector3 Forward(float yaw,float pitch) =>
        new(MathF.Sin(yaw)*MathF.Cos(pitch),MathF.Sin(pitch),MathF.Cos(yaw)*MathF.Cos(pitch));

    internal static Quaternion FromAngles(float yaw,float pitch,float roll=0f) =>
        Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(yaw,-pitch,roll));
}
