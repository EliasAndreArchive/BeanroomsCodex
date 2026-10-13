using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Beanrooms;

internal sealed partial class GameWindow
{
    void GenerateWorld()
    {
        probeDirty=true;
        physics.Clear();
        var layout=WorldLayout.Generate(seed);
        grid=layout.Grid;
        boxes.Clear();
        boxes.AddRange(layout.Walls);
        WorldProps.Place(physics,boxes);
        audio?.SetWorld(boxes);
        localMonster = new MonsterBrain(seed,boxes,menu.MonsterSettings);
        visibleMonster = localMonster.State;
        Gl.BindTexture(0x0DE1,worldTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        var handle = GCHandle.Alloc(grid,GCHandleType.Pinned);
        try { Gl.TexImage2D(0x0DE1,0,0x8814,Grid,Grid,0,0x1908,0x1406,handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
        RebuildFixtureMap();
        Vector2 clearSpawn=WorldLayout.FindClearSpawn(boxes,new Vector2(96f,96f),Radius);
        camera = new Vector3(clearSpawn.X,1.68f,clearSpawn.Y);
        smoothYaw = yaw;
        smoothPitch = pitch;
        lookInitialized = true;
        menu.BodyCount=physics.Bodies.Count; menu.Dirty=true;
    }
    static float FixtureHash(int x,int z,int worldSeed)
    {
        unchecked
        {
            uint value=(uint)x*1597334677u ^ (uint)z*3812015801u ^ (uint)worldSeed*95828261u;
            value ^= value >> 16; value *= 2246822519u;
            value ^= value >> 13; value *= 3266489917u;
            value ^= value >> 16;
            return (value >> 8)*(1f/16777216f);
        }
    }
    bool FixtureClear(int x,int z)
    {
        float cx=x*8f, cz=z*8f, minX=cx-1f, minZ=cz-1f, maxX=cx+1f, maxZ=cz+1f;
        int x0=Math.Clamp((int)MathF.Floor(minX/Cell),0,Grid-1);
        int z0=Math.Clamp((int)MathF.Floor(minZ/Cell),0,Grid-1);
        int x1=Math.Clamp((int)MathF.Floor(maxX/Cell),0,Grid-1);
        int z1=Math.Clamp((int)MathF.Floor(maxZ/Cell),0,Grid-1);
        for(int zz=z0;zz<=z1;zz++) for(int xx=x0;xx<=x1;xx++)
        {
            int offset=(zz*Grid+xx)*4;
            if(grid[offset]>=0f && maxX>grid[offset] && minX<grid[offset+2] &&
                maxZ>grid[offset+1] && minZ<grid[offset+3]) return false;
        }
        return true;
    }
    void RebuildFixtureMap()
    {
        probeDirty=true;
        Array.Clear(fixturePixels);
        activeFixtures.Clear();
        for(int z=0;z<FixtureGrid;z++) for(int x=0;x<FixtureGrid;x++)
        {
            bool enabled=(x==12 && z==12) ||
                (FixtureHash(x/3+47,z/3-29,seed)>=0.26f &&
                 FixtureHash(x,z,seed)>1f-menu.LightDensity);
            int offset=(z*FixtureGrid+x)*4;
            bool active=enabled && FixtureClear(x,z);
            fixtureBase[z*FixtureGrid+x]=active;
            fixturePixels[offset]=active ? 1f : 0f;
            fixturePixels[offset+2]=active ? 1f : 0f;
            if(active) activeFixtures.Add(new Vector2(x*8f,z*8f));
            fixturePixels[offset+1]=FixtureHash(x+91,z-37,seed)<0.08f ? 1f : 0f;
        }
        Bind(4,fixtureTexture);
        Gl.TexParameteri(0x0DE1,0x2801,0x2600); Gl.TexParameteri(0x0DE1,0x2800,0x2600);
        Gl.TexParameteri(0x0DE1,0x2802,0x812F); Gl.TexParameteri(0x0DE1,0x2803,0x812F);
        var handle=GCHandle.Alloc(fixturePixels,GCHandleType.Pinned);
        try { Gl.TexImage2D(0x0DE1,0,0x8814,FixtureGrid,FixtureGrid,0,0x1908,0x1406,handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
        appliedLightDensity=menu.LightDensity;
    }

    void UpdateFixtureFlicker()
    {
        double now=watch.Elapsed.TotalSeconds;
        flicker.UpdateRandom(now,network.Connected);
        flicker.UpdateChase(now,chaseLocked);
        float redChance=0.08f;
        redChance+=(FlickerProfile.ChaseRedChance-0.08f)*chaseBlend;
        bool changed=false;
        for(int z=0;z<FixtureGrid;z++) for(int x=0;x<FixtureGrid;x++)
        {
            int index=z*FixtureGrid+x;
            float on=fixtureBase[index] && flicker.IsOn(x,z,seed,now) ? 1f : 0f;
            if(fixturePixels[index*4]!=on) { fixturePixels[index*4]=on;changed=true; }
            float red=FixtureHash(x+91,z-37,seed)<redChance ? 1f : 0f;
            if(fixturePixels[index*4+1]!=red) { fixturePixels[index*4+1]=red;changed=true; }
        }
        if(!changed) return;
        probeDirty=true;
        activeFixtures.Clear();
        for(int z=0;z<FixtureGrid;z++) for(int x=0;x<FixtureGrid;x++)
            if(fixturePixels[(z*FixtureGrid+x)*4]>0.5f)
                activeFixtures.Add(new Vector2(x*8f,z*8f));
        Bind(4,fixtureTexture);
        var handle=GCHandle.Alloc(fixturePixels,GCHandleType.Pinned);
        try { Gl.TexImage2D(0x0DE1,0,0x8814,FixtureGrid,FixtureGrid,0,0x1908,0x1406,handle.AddrOfPinnedObject()); }
        finally { handle.Free(); }
    }
    void Regenerate()
    {
        seed = Random.Shared.Next(1,1_000_000_000);
        GenerateWorld();
        motion.Reset();
        BeginSpawn(false);
    }

    void StartSoloSession()
    {
        DisconnectNetwork();
        sessionPhase=GameSessionPhase.PlayingSolo;
        Regenerate();
        menu.LeaveTitle();
        CloseSettings();
    }

    void LeaveSession()
    {
        if(network.Connected) network.Send(new NetMessage { Type="release" });
        physics.Release();
        physics.Clear();
        DisconnectNetwork();
        sessionPhase=GameSessionPhase.MainMenu;
        localMonster=null;
        visibleMonster=null;
        networkCubes=[];
        remotePositions.Clear();remoteYaws.Clear();remoteColors.Clear();remoteNames.Clear();
        remoteFootsteps.Clear();
        motion.Reset();
        crouchToggle=false;rotatingHeld=false;networkHolding=false;networkHeldId=0;
        chaseLocked=false;chaseBlend=0f;mouseYawDelta=0f;
        menu.BodyCount=0;
        menu.ShowTitle();
        audio?.SetWorld([]);
        wantsCapture=false;
        SuspendMouse();
    }

    void BeginSpawn(bool monsterHit)
    {
        double now=watch.Elapsed.TotalSeconds;
        spawnTransition.Begin(now,monsterHit);
        if(monsterHit || now-lastSpawnSound>0.5)
        {
            audio?.PlaySpawn();
            lastSpawnSound=now;
        }
    }

    void RespawnLocal()
    {
        if(visibleMonster==null) return;
        Vector2 point=WorldLayout.FindSafeRespawn(boxes,
            new Vector2(visibleMonster.X,visibleMonster.Z),Radius,Random.Shared);
        physics.Release();
        camera=new Vector3(point.X,1.68f,point.Y);
        motion.Reset();
        crouchToggle=false;
        localRespawnProtectedUntil=watch.Elapsed.TotalSeconds+2.5;
        BeginSpawn(true);
    }

    void CaptureMouse() { if (ready && wantsCapture && !active && Focused) { active=true; CaptureCursor(); } }
    void SuspendMouse() { if (active) { active=false; ReleaseCursor(); } keys.Clear(); }
    void ReleaseMouse() { wantsCapture=false; SuspendMouse(); }
    void ToggleSettings(bool back=false)
    {
        bool opening=!menu.Open;
        networkHolding=false;

        network.Send(new NetMessage { Type="release" });
        physics.Release();
        if(back) menu.Back(); else menu.Toggle();
        if(opening && !menu.TitleScreen) menu.SetPage(9);
        if(menu.Open) { wantsCapture=false; SuspendMouse(); }
        else CloseSettings();
    }
    void CloseSettings()
    {
        wantsCapture=HasPlayer;
        if(wantsCapture) BeginInvoke(new Action(CaptureMouse));
    }
    void ConnectOnline()
    {
        if(HasPlayer) return;
        menu.FinishEditing();
        if(string.IsNullOrWhiteSpace(menu.JoinKey))
        {
            menu.NetworkStatus="ENTER KEY"; menu.Dirty=true;
            return;
        }
        DisconnectNetwork();
        network=new PhotonNetClient(menu.JoinKey);
        networkStarted=true;
        sessionPhase=GameSessionPhase.JoiningRoom;
        menu.NetworkStatus="CONNECTING";
        menu.Dirty=true;
        network.Start();
    }
    void CancelJoin()
    {
        if(sessionPhase!=GameSessionPhase.JoiningRoom) return;
        DisconnectNetwork();
        sessionPhase=GameSessionPhase.MainMenu;
    }
    void DisconnectNetwork()
    {
        network.Dispose();
        network=new SoloNetClient();
        networkStarted=false;
        networkHolding=false;

        menu.NetworkConnected=false;
        menu.NetworkId=0;
        menu.NetworkHost=0;
        menu.NetworkPlayers=1;
        menu.NetworkStatus="OFFLINE";
        menu.Dirty=true;
    }
}
