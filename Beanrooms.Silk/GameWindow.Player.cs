using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beanrooms;

internal sealed partial class GameWindow
{
    protected override void OnRawMouse(float dx,float dy)
    {
        if(active && Focused) MoveMouse((int)MathF.Round(dx),(int)MathF.Round(dy));
    }
    void MoveMouse(int dx,int dy)
    {
        if(dx==0 && dy==0) return;
        dx=Math.Clamp(dx,-100,100); dy=Math.Clamp(dy,-100,100);
        if(rotatingHeld && (physics.Held!=null || networkHolding || networkCubes.Any(cube=>cube.HolderId==network.Id && network.Id!=0)))
        {
            grabYaw=MathF.IEEERemainder(grabYaw+dx*menu.MouseSensitivity,MathF.Tau);
            grabPitch=Math.Clamp(grabPitch-dy*menu.MouseSensitivity,-1.5f,1.5f);
            return;
        }
        float yawChange=dx*menu.MouseSensitivity;
        yaw += yawChange;
        mouseYawDelta += yawChange;
        pitch=Math.Clamp(pitch-dy*menu.MouseSensitivity,-1.35f,1.35f);
    }

    static Vector3 CubeLocalExtents(CubeState cube)
        => new Vector3(cube.HalfSize);

    bool Collides(float x,float z,float footHeight)
    {
        if(x<4+Radius||z<4+Radius||x>Grid*Cell-4-Radius||z>Grid*Cell-4-Radius) return true;
        foreach(var b in boxes) if(x>b.Left-Radius&&x<b.Right+Radius&&z>b.Top-Radius&&z<b.Bottom+Radius) return true;
        if(network.Connected)
        {
            foreach(CubeState cube in networkCubes)
            {
                Quaternion rotation=new(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
                if(rotation.LengthSquared()<.001f) rotation=Quaternion.Identity;
                Vector3 extent=PhysicsBodies.RotatedExtents(CubeLocalExtents(cube),rotation);
                if(MathF.Abs(x-cube.X)<extent.X+Radius && MathF.Abs(z-cube.Z)<extent.Z+Radius &&
                   cube.Y+extent.Y>footHeight+.025f && cube.Y-extent.Y<footHeight+2.1f) return true;
            }
        }
        else if(physics.CollidesPlayer(x,z,Radius,footHeight)) return true;
        return false;
    }

    void ViewPose(out Vector3 eye,out Vector3 forward)
    {
        eye=camera+motion.ViewOffset;
        float viewYaw=smoothYaw+motion.YawOffset;
        float viewPitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f);
        forward=CameraOrientation.Forward(viewYaw,viewPitch);
    }
    Quaternion ViewRotation()
    {
        float viewYaw=smoothYaw+motion.YawOffset;
        float viewPitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f);
        return CameraOrientation.FromAngles(viewYaw,viewPitch,motion.Roll);
    }
    void SendPlayerState()
    {
        if(!network.Connected) return;
        ViewPose(out Vector3 eye,out _);
        network.Send(new NetMessage { Type="state",X=eye.X,Y=eye.Y,Z=eye.Z,
            MoveSpeed=motion.Speed,SprintBlend=motion.SprintBlend,CrouchBlend=motion.CrouchBlend,
            Yaw=smoothYaw+motion.YawOffset,Pitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f),
            HoldDistance=ObjectInteractionProfile.HoldDistance,PickupRange=ObjectInteractionProfile.PickupRange,
            GrabYaw=grabYaw,GrabPitch=grabPitch,
            SpringFrequency=ObjectInteractionProfile.SpringFrequency,SpringDamping=ObjectInteractionProfile.SpringDamping,
            PidSmoothing=ObjectInteractionProfile.Smoothing,SlideResistance=ObjectInteractionProfile.SlideResistance,
            MonsterTuning=menu.MonsterSettings.Clone(),
            Name=menu.PlayerName,RespawnId=respawnId,
            R=menu.PlayerRed,G=menu.PlayerGreen,B=menu.PlayerBlue });
    }

}
