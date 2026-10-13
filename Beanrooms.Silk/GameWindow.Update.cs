using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beanrooms;

internal sealed partial class GameWindow
{
    void UpdateGame(float dt)
    {
        network.Service(dt);
        if(sessionPhase==GameSessionPhase.PlayingOnline && !network.Connected)
        {
            LeaveSession();
            return;
        }
        string connectionStatus=network.Status;
        if(networkStarted && menu.NetworkStatus!=connectionStatus)
        { menu.NetworkStatus=connectionStatus;menu.Dirty=true; }
        NetMessage? lightEvent=network.ConsumeFlicker();
        if(lightEvent!=null) flicker.Trigger(watch.Elapsed.TotalSeconds,lightEvent.BlackoutAfter);
        NetMessage? world=network.ConsumeWorld();
        if(world!=null)
        {
            flicker.Restore(watch.Elapsed.TotalSeconds,world.Blackout,world.BlackoutAfter);
            if(world.FlickerAge>=0f && world.FlickerAge<FlickerProfile.EventDuration)
                flicker.Trigger(watch.Elapsed.TotalSeconds-world.FlickerAge,world.BlackoutAfter);
            if(seed!=world.Seed) { seed=world.Seed; GenerateWorld(); }
            camera=new Vector3(world.X,world.Y,world.Z);
            motion.Reset();
            networkCubes=[];

            visibleMonster=null;
            remotePositions.Clear();
            remoteYaws.Clear();
            remoteColors.Clear();
            remoteNames.Clear();
            menu.BodyCount=0; menu.Dirty=true;
            respawnId=world.RespawnId;
            nextNetworkSend=0;
            BeginSpawn(false);
            if(sessionPhase==GameSessionPhase.JoiningRoom)
            {
                sessionPhase=GameSessionPhase.PlayingOnline;
                menu.LeaveTitle();
                CloseSettings();
            }
        }
        if(!HasPlayer || (sessionPhase==GameSessionPhase.PlayingSolo && menu.Open))
        { mouseYawDelta=0f;return; }
        NetMessage? respawn=network.ConsumeRespawn();
        if(respawn!=null)
        {
            respawnId=respawn.RespawnId;
            camera=new Vector3(respawn.X,respawn.Y,respawn.Z);
            motion.Reset();
            crouchToggle=false;
            networkHolding=false;

            physics.Release();
            nextNetworkSend=0;
            BeginSpawn(true);
        }
        Vector2 input=Vector2.Zero;
        if(active)
        {
            if(keys.Contains(Keys.W)||keys.Contains(Keys.Up)) input.Y+=1f;
            if(keys.Contains(Keys.S)||keys.Contains(Keys.Down)) input.Y-=1f;
            if(keys.Contains(Keys.D)||keys.Contains(Keys.Right)) input.X+=1f;
            if(keys.Contains(Keys.A)||keys.Contains(Keys.Left)) input.X-=1f;
        }
        bool crouching=crouchToggle || keys.Contains(Keys.ControlKey) || keys.Contains(Keys.LControlKey) || keys.Contains(Keys.RControlKey);
        bool sprinting=keys.Contains(Keys.ShiftKey) || keys.Contains(Keys.LShiftKey) || keys.Contains(Keys.RShiftKey);
        motion.Update(dt,input,sprinting,crouching,mouseYawDelta,yaw,menu,ref camera,Collides,active);
        mouseYawDelta=0f;
        if(!lookInitialized) { smoothYaw=yaw; smoothPitch=pitch; lookInitialized=true; }
        float lookBlend=1f-MathF.Exp(-MathF.Max(menu.CameraSmooth,0.1f)*dt);
        smoothYaw += MathF.Atan2(MathF.Sin(yaw-smoothYaw),MathF.Cos(yaw-smoothYaw))*lookBlend;
        smoothPitch += (pitch-smoothPitch)*lookBlend;
        ViewPose(out Vector3 eye,out Vector3 forward);
        if(network.Connected)
        {
            double now=watch.Elapsed.TotalSeconds;
            if(now>=nextNetworkSend) { SendPlayerState(); nextNetworkSend=now+0.05; }
            var snapshot=network.Snapshot();
            int hostId=snapshot.HostId;
            if(hostId==0 && snapshot.Players.Length>0)
                hostId=snapshot.Players.Min(player=>player.Id);
            int count=snapshot.Players.Length;
            if(!menu.NetworkConnected || menu.NetworkId!=network.Id || menu.NetworkHost!=hostId || menu.NetworkPlayers!=count)
            { menu.NetworkConnected=true;menu.NetworkId=network.Id;menu.NetworkHost=hostId;menu.NetworkPlayers=count;menu.Dirty=true; }
            bool writable=hostId==network.Id;
            if(menu.MonsterControlsWritable!=writable) { menu.MonsterControlsWritable=writable;menu.Dirty=true; }
            if(!writable&&snapshot.MonsterTuning!=null&&menu.MonsterSettings.CopyFrom(snapshot.MonsterTuning))
                menu.Dirty=true;
            if(snapshot.Monster!=null)
            {
                MonsterState target=snapshot.Monster;
                if(visibleMonster==null) visibleMonster=new MonsterState
                    { X=target.X,Y=target.Y,Z=target.Z,Yaw=target.Yaw,Mode=target.Mode };
                float monsterBlend=1f-MathF.Exp(-15f*dt);
                visibleMonster.X+=(target.X-visibleMonster.X)*monsterBlend;
                visibleMonster.Y+=(target.Y-visibleMonster.Y)*monsterBlend;
                visibleMonster.Z+=(target.Z-visibleMonster.Z)*monsterBlend;
                visibleMonster.Yaw+=MathF.Atan2(MathF.Sin(target.Yaw-visibleMonster.Yaw),MathF.Cos(target.Yaw-visibleMonster.Yaw))*monsterBlend;
                visibleMonster.Mode=target.Mode;
                visibleMonster.TargetId=target.TargetId;
                visibleMonster.GoalX=target.GoalX;visibleMonster.GoalZ=target.GoalZ;
                visibleMonster.HasLastSeen=target.HasLastSeen;
                visibleMonster.LastX=target.LastX;visibleMonster.LastZ=target.LastZ;
                visibleMonster.Route=target.Route;
            }
            CubeState[] cubes=snapshot.Cubes;
            NetMessage? pickupResult=network.ConsumePickupResult();
            if(pickupResult!=null && pickupResult.GrabSequence==grabSequence)
            {
                networkHeldId=networkHolding?pickupResult.Action:0;
                if(pickupResult.Action==0) networkHolding=false;
            }
            if(networkCubes.Length!=cubes.Length || cubes.Where((cube,i)=>cube.Id!=networkCubes[i].Id).Any())
                networkCubes=cubes.Select(CloneCube).ToArray();
            else for(int i=0;i<cubes.Length;i++)
            {
                CubeState target=cubes[i], current=networkCubes[i];
                if(current.Id!=target.Id) { networkCubes[i]=CloneCube(target);continue; }
                float blend=1f-MathF.Exp(-24f*dt);
                current.X+= (target.X-current.X)*blend;
                current.Y+= (target.Y-current.Y)*blend;
                current.Z+= (target.Z-current.Z)*blend;
                Quaternion qa=new(current.Qx,current.Qy,current.Qz,current.Qw);
                Quaternion qb=new(target.Qx,target.Qy,target.Qz,target.Qw);
                Quaternion q=Quaternion.Slerp(qa,qb,blend);
                current.Qx=q.X;current.Qy=q.Y;current.Qz=q.Z;current.Qw=q.W;
                current.HalfSize=target.HalfSize;current.Emissive=target.Emissive;current.Material=target.Material;
                current.HolderId=target.HolderId;
                current.Vx=target.Vx;current.Vy=target.Vy;current.Vz=target.Vz;current.Grounded=target.Grounded;
                current.Ax=target.Ax;current.Ay=target.Ay;current.Az=target.Az;
                current.SlipSpeed=target.SlipSpeed;
                current.R=target.R;current.G=target.G;current.B=target.B;
            }
            var seen=new HashSet<int>();
            remoteFootsteps.Clear();
            foreach(PlayerState player in snapshot.Players)
            {
                if(player.Id==network.Id) continue;
                seen.Add(player.Id);
                Vector3 target=new(player.X,player.Y,player.Z);
                remotePositions[player.Id]=remotePositions.TryGetValue(player.Id,out Vector3 current)
                    ? Vector3.Lerp(current,target,1f-MathF.Exp(-16f*dt)) : target;
                float targetYaw=player.Yaw;
                remoteYaws[player.Id]=remoteYaws.TryGetValue(player.Id,out float previousYaw)
                    ? previousYaw+MathF.Atan2(MathF.Sin(targetYaw-previousYaw),MathF.Cos(targetYaw-previousYaw))*(1f-MathF.Exp(-16f*dt))
                    : targetYaw;
                remoteColors[player.Id]=new Vector3(player.R,player.G,player.B);
                remoteNames[player.Id]=player.Name;
                remoteFootsteps.Add(new FootstepSource(player.Id,remotePositions[player.Id],player.MoveSpeed,
                    player.SprintBlend,player.CrouchBlend));
            }
            foreach(int id in remotePositions.Keys.Where(id=>!seen.Contains(id)).ToArray())
            { remotePositions.Remove(id); remoteYaws.Remove(id);remoteColors.Remove(id);remoteNames.Remove(id); }
            if(menu.BodyCount!=networkCubes.Length) { menu.BodyCount=networkCubes.Length; menu.Dirty=true; }
            return;
        }
        networkCubes=[];
        remotePositions.Clear();
        remoteYaws.Clear();
        remoteColors.Clear();
        remoteNames.Clear();
        remoteFootsteps.Clear();
        if(menu.NetworkConnected) { menu.NetworkConnected=false;menu.NetworkId=0;menu.NetworkHost=0;menu.NetworkPlayers=1;menu.Dirty=true; }
        networkHolding=false;

        if(!menu.MonsterControlsWritable) { menu.MonsterControlsWritable=true;menu.Dirty=true; }
        physics.PickupRange=ObjectInteractionProfile.PickupRange;
        physics.HoldDistance=ObjectInteractionProfile.HoldDistance;
        physics.RotateYaw=grabYaw;physics.RotatePitch=grabPitch;
        physics.Frequency=ObjectInteractionProfile.SpringFrequency;
        physics.Damping=ObjectInteractionProfile.SpringDamping;
        physics.TargetSmoothing=ObjectInteractionProfile.Smoothing;
        physics.SlideResistance=ObjectInteractionProfile.SlideResistance;
        physics.Update(dt,eye,forward,ViewRotation(),boxes,camera);
        localMonster?.Update(dt,[new PlayerState { Id=1,X=eye.X,Y=eye.Y,Z=eye.Z,Yaw=smoothYaw }],physics.Bodies);
        visibleMonster=localMonster?.State;
        if(menu.MonsterSettings.Enabled && visibleMonster!=null &&
           watch.Elapsed.TotalSeconds>=localRespawnProtectedUntil &&
           Vector2.DistanceSquared(new Vector2(camera.X,camera.Z),new Vector2(visibleMonster.X,visibleMonster.Z))<=0.81f)
        {
            RespawnLocal();
            ViewPose(out eye,out forward);
        }
    }

    static CubeState CloneCube(CubeState source) => new()
    {
        Id=source.Id,HolderId=source.HolderId,
        X=source.X,Y=source.Y,Z=source.Z,Qx=source.Qx,Qy=source.Qy,Qz=source.Qz,Qw=source.Qw,
        Vx=source.Vx,Vy=source.Vy,Vz=source.Vz,Grounded=source.Grounded,
        Ax=source.Ax,Ay=source.Ay,Az=source.Az,SlipSpeed=source.SlipSpeed,
        HalfSize=source.HalfSize,Emissive=source.Emissive,Material=source.Material,R=source.R,G=source.G,B=source.B
    };
}
