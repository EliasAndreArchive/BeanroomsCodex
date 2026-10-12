using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Beanrooms;

internal sealed partial class GameWindow
{
    void Bind(int unit,int texture) { Gl.ActiveTexture((uint)(0x84C0+unit)); Gl.BindTexture(0x0DE1,texture); }
    static void Uniform1(int p,string name,int value) { Gl.Uniform1i(Gl.GetUniformLocation(p,name),value); }
    static void Uniform1f(int p,string name,float value) { Gl.Uniform1f(Gl.GetUniformLocation(p,name),value); }
    static void Uniform2(int p,string name,float x,float y) { Gl.Uniform2f(Gl.GetUniformLocation(p,name),x,y); }
    static bool ProjectDebug(Vector3 point,Vector3 eye,Vector3 right,Vector3 up,Vector3 forward,out PointF screen)
    {
        Vector3 delta=point-eye;
        float depth=Vector3.Dot(delta,forward);
        if(depth<0.25f) { screen=default;return false; }
        float x=160f+Vector3.Dot(delta,right)*240f/(depth*1.55f);
        float y=120f-Vector3.Dot(delta,up)*240f/(depth*1.55f);
        screen=new PointF(x,y);
        return x>=-40f&&x<=360f&&y>=-40f&&y<=280f;
    }
    static bool ProjectCubeFrame(Vector3 position,Quaternion rotation,float half,
        Vector3 eye,Vector3 right,Vector3 up,Vector3 forward,out RectangleF bounds)
    {
        float minX=float.PositiveInfinity,minY=float.PositiveInfinity;
        float maxX=float.NegativeInfinity,maxY=float.NegativeInfinity;
        for(int sx=-1;sx<=1;sx+=2) for(int sy=-1;sy<=1;sy+=2) for(int sz=-1;sz<=1;sz+=2)
        {
            Vector3 corner=position+Vector3.Transform(new Vector3(sx*half,sy*half,sz*half),rotation);
            if(Vector3.Dot(corner-eye,forward)<0.25f) { bounds=default;return false; }
            ProjectDebug(corner,eye,right,up,forward,out PointF screen);
            minX=MathF.Min(minX,screen.X);minY=MathF.Min(minY,screen.Y);
            maxX=MathF.Max(maxX,screen.X);maxY=MathF.Max(maxY,screen.Y);
        }
        if(maxX<0f||minX>320f||maxY<0f||minY>240f) { bounds=default;return false; }
        int left=Math.Clamp((int)MathF.Floor(minX)-1,1,316);
        int top=Math.Clamp((int)MathF.Floor(minY)-1,1,236);
        int rightEdge=Math.Clamp((int)MathF.Ceiling(maxX)+1,left+2,319);
        int bottomEdge=Math.Clamp((int)MathF.Ceiling(maxY)+1,top+2,239);
        bounds=RectangleF.FromLTRB(left,top,rightEdge,bottomEdge);
        return true;
    }
    void PrepareMonsterOverlay(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        if(visibleMonster==null) { menu.MonsterTargetText="TARGET NONE";menu.MonsterSegments=[];menu.MonsterPoints=[];return; }
        if(!menu.MonsterSettings.Enabled)
        {
            menu.MonsterSegments=[];menu.MonsterPoints=[];
            menu.MonsterTargetText="MONSTER OFF";menu.Dirty=true;
            return;
        }
        MonsterState monster=visibleMonster;
        menu.MonsterTargetText=monster.TargetId==0?"TARGET NONE":
            monster.Mode==2?$"TARGET P{monster.TargetId}":$"LAST P{monster.TargetId}";
        if(!menu.MonsterDebug||!menu.ShowDebugOverlay) { menu.MonsterSegments=[];menu.MonsterPoints=[];return; }
        var segments=new List<(PointF A,PointF B,Color Ink)>();
        var points=new List<(PointF At,string Label,Color Ink)>();
        void Mark(Vector3 position,string label,Color ink)
        {
            if(ProjectDebug(position,eye,right,up,forward,out PointF screen)) points.Add((screen,label,ink));
        }
        void Segment(Vector3 from,Vector3 to,Color ink)
        {
            if(ProjectDebug(from,eye,right,up,forward,out PointF a)&&
               ProjectDebug(to,eye,right,up,forward,out PointF b)) segments.Add((a,b,ink));
        }
        Vector3 head=new(monster.X,1.55f,monster.Z);
        Vector3 previous=new(monster.X,0.3f,monster.Z);
        Color routeInk=Color.FromArgb(75,225,155);
        foreach(NavPoint waypoint in monster.Route)
        {
            Vector3 next=new(waypoint.X,0.3f,waypoint.Z);
            Segment(previous,next,routeInk);
            Mark(next,".",routeInk);
            previous=next;
        }
        Mark(head,"EYE",Color.FromArgb(255,100,105));
        Mark(new Vector3(monster.GoalX,0.35f,monster.GoalZ),"GOAL",Color.FromArgb(90,205,255));
        if(monster.HasLastSeen)
            Mark(new Vector3(monster.LastX,0.35f,monster.LastZ),"LAST",Color.FromArgb(255,190,85));
        if(monster.TargetId>0 && monster.Mode==2)
        {
            Vector3? targetPlayer=null;
            if(!network.Connected||monster.TargetId==network.Id) targetPlayer=camera;
            else if(remotePositions.TryGetValue(monster.TargetId,out Vector3 remote)) targetPlayer=remote;
            if(targetPlayer.HasValue)
            {
                Vector3 player=new(targetPlayer.Value.X,1.5f,targetPlayer.Value.Z);
                Segment(head,player,Color.FromArgb(255,100,105));
                Mark(player,$"P{monster.TargetId}",Color.FromArgb(255,100,105));
            }
        }
        menu.MonsterSegments=segments.ToArray();menu.MonsterPoints=points.ToArray();
        menu.Dirty=true;
    }
    bool ClearNameSight(Vector3 from,Vector3 to)
    {
        Vector2 start=new(from.X,from.Z), end=new(to.X,to.Z), delta=end-start;
        foreach(RectangleF wall in boxes)
        {
            float lo=0f,hi=1f;
            if(!Slab(start.X,delta.X,wall.Left,wall.Right,ref lo,ref hi) ||
               !Slab(start.Y,delta.Y,wall.Top,wall.Bottom,ref lo,ref hi)) continue;
            if(lo<0.98f && hi>0.02f) return false;
        }
        return true;
    }
    static bool Slab(float start,float delta,float min,float max,ref float lo,ref float hi)
    {
        if(MathF.Abs(delta)<0.00001f) return start<min||start>max?false:true;
        float a=(min-start)/delta,b=(max-start)/delta;
        lo=MathF.Max(lo,MathF.Min(a,b));hi=MathF.Min(hi,MathF.Max(a,b));
        return lo<=hi;
    }
    void PreparePlayerTags(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        if(remotePositions.Count==0)
        { if(menu.PlayerTags.Length>0) { menu.PlayerTags=[];menu.Dirty=true; } return; }
        var tags=new List<(PointF At,string Label,Color Ink)>();
        foreach(var player in remotePositions.Take(4))
        {
            Vector3 head=player.Value+new Vector3(0,0.53f,0);
            if(Vector3.DistanceSquared(eye,head)>400f || !ClearNameSight(eye,head) ||
               !ProjectDebug(head,eye,right,up,forward,out PointF screen)) continue;
            Vector3 c=remoteColors.TryGetValue(player.Key,out Vector3 color)?color:Vector3.One;
            tags.Add((screen,remoteNames.TryGetValue(player.Key,out string? name)?name:$"BEAN{player.Key}",
                Color.FromArgb((int)(Math.Clamp(c.X,0f,1f)*255),(int)(Math.Clamp(c.Y,0f,1f)*255),(int)(Math.Clamp(c.Z,0f,1f)*255))));
        }
        menu.PlayerTags=tags.ToArray();menu.Dirty=true;
    }
    void PrepareObjectFrames(Vector3 eye,Vector3 right,Vector3 up,Vector3 forward)
    {
        RigidCube[] localCubes=network.Connected?[]:physics.Bodies.ToArray();
        int count=network.Connected?networkCubes.Length:localCubes.Length;
        if(!menu.ShowObjectFrames||count==0)
        {
            if(menu.ObjectFrames.Length>0) { menu.ObjectFrames=[];menu.ObjectPrompt="";menu.Dirty=true; }
            return;
        }
        int heldId=network.Connected
            ? networkHolding?networkHeldId:networkCubes.FirstOrDefault(cube=>cube.HolderId==network.Id)?.Id??0
            : physics.Held?.Id??0;
        float nearest=heldId==0
            ? MathF.Min(menu.PickupRange,PhysicsBodies.WallRay(eye,forward,boxes,menu.PickupRange))
            : float.PositiveInfinity;
        Vector3 hitPosition=default;
        Quaternion hitRotation=Quaternion.Identity;
        float hitHalf=0f;
        bool found=false;
        double now=watch.Elapsed.TotalSeconds;
        for(int i=0;i<count;i++)
        {
            Vector3 position;
            Quaternion rotation;
            float half;
            int id;
            if(network.Connected)
            {
                CubeState cube=networkCubes[i];
                position=new Vector3(cube.X,cube.Y,cube.Z);
                rotation=new Quaternion(cube.Qx,cube.Qy,cube.Qz,cube.Qw);
                half=cube.HalfSize;
                id=cube.Id;
            }
            else
            {
                RigidCube cube=localCubes[i];
                position=cube.Position;rotation=cube.Rotation;half=cube.HalfSize;id=cube.Id;
            }
            if(heldId!=0 && id!=heldId) continue;
            if(rotation.LengthSquared()<.001f) rotation=Quaternion.Identity;
            Quaternion inverse=Quaternion.Inverse(rotation);
            float t=PhysicsBodies.RayBox(Vector3.Transform(eye-position,inverse),
                Vector3.Transform(forward,inverse),Vector3.Zero,new Vector3(half));
            if(heldId==0 && (t<0f||t>=nearest)) continue;
            nearest=t;hitPosition=position;hitRotation=rotation;
            hitHalf=half*CubeVisualScale(i,id,now);found=true;
        }
        menu.ObjectFrames=found&&ProjectCubeFrame(hitPosition,hitRotation,hitHalf,eye,right,up,forward,out RectangleF bounds)
            ?[bounds]:[];
        menu.ObjectPrompt=menu.ObjectFrames.Length==0?"":heldId!=0?"LMB HOLD":"LMB GRAB";
        menu.Dirty=true;
    }
    void UploadOverlay()
    {
        using var image = menu.Draw();
        var data = image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            Bind(1,overlayTexture);
            Gl.TexImage2D(0x0DE1,0,0x8058,image.Width,image.Height,0,0x80E1,0x1401,data.Scan0);
        }
        finally { image.UnlockBits(data); }
    }
    void RunPostProcessing()
    {
        double now=watch.Elapsed.TotalSeconds;
        float dt=(float)Math.Clamp(now-lastExposureTime,0.0,0.1);
        lastExposureTime=now;
        if(menu.PostEnabled && menu.AutoExposure)
        {
            Bind(0,sceneTexture);
            Gl.GenerateMipmap(0x0DE1);
            int previous=exposureCurrent==0?exposureA:exposureB;
            int next=exposureCurrent==0?exposureB:exposureA;
            Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,next,0);
            Gl.Viewport(0,0,1,1);
            Gl.UseProgram(exposureProgram);
            Bind(0,sceneTexture);Uniform1(exposureProgram,"sceneTex",0);
            Bind(1,previous);Uniform1(exposureProgram,"previousExposure",1);
            Uniform1f(exposureProgram,"brightenSpeed",menu.BrightenSpeed);
            Uniform1f(exposureProgram,"darkenSpeed",menu.DarkenSpeed);
            Uniform1f(exposureProgram,"deltaTime",dt);
            Gl.DrawArrays(0x0004,0,3);
            exposureCurrent=1-exposureCurrent;
        }
        if(menu.PostEnabled && menu.Bloom && menu.BloomStrength>0.001f)
        {
            int w=Math.Max(1,width/2),h=Math.Max(1,height/2);
            Gl.Viewport(0,0,w,h);
            Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,bloomA,0);
            Gl.UseProgram(bloomExtractProgram);
            Bind(0,sceneTexture);Uniform1(bloomExtractProgram,"sceneTex",0);
            Uniform1f(bloomExtractProgram,"threshold",menu.BloomThreshold);
            Gl.DrawArrays(0x0004,0,3);
            BlurBloom(bloomA,bloomB,1f,0f,w,h);
            BlurBloom(bloomB,bloomA,0f,1f,w,h);
        }
        Gl.BindFramebuffer(0x8D40,0);
    }
    void BlurBloom(int source,int destination,float dx,float dy,int w,int h)
    {
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,destination,0);
        Gl.UseProgram(bloomBlurProgram);
        Bind(0,source);Uniform1(bloomBlurProgram,"sourceTex",0);
        Uniform2(bloomBlurProgram,"direction",dx,dy);
        Uniform2(bloomBlurProgram,"texelSize",1f/w,1f/h);
        Uniform1f(bloomBlurProgram,"radius",menu.BloomRadius);
        Gl.DrawArrays(0x0004,0,3);
    }

    void Render()
    {
        if(menu.LightDensity!=appliedLightDensity) RebuildFixtureMap();
        UpdateFixtureFlicker();
        if(menu.Vsync != appliedVsync)
        {
            SetVsync(menu.Vsync);
            appliedVsync=menu.Vsync;
        }
        if(menu.ResolutionIndex != appliedResolution)
        {
            appliedResolution=menu.ResolutionIndex;
            int scale=appliedResolution+1;
            ResizeRender(scale==1?320:scale==2?480:640,scale==1?240:scale==2?360:480);
        }
        if(menu.RenderMode==1)
        {
            int settings=ProbeSettingsSignature();
            if(settings!=lastProbeSettings) probeDirty=true;
            if(probeDirty) { RenderProbes();lastProbeSettings=settings; }
        }
        Gl.BindFramebuffer(0x8D40,framebuffer);
        Gl.FramebufferTexture2D(0x8D40,0x8CE0,0x0DE1,renderTexture,0);
        Gl.Viewport(0,0,width,height);
        Gl.UseProgram(pathProgram);
        Bind(0,worldTexture); Bind(1,wallTexture); Bind(2,floorTexture); Bind(3,ceilingTexture); Bind(4,fixtureTexture); Bind(5,faceTexture); Bind(6,eyeTexture);
        Bind(7,probeDirect);Bind(8,probeResult);
        Uniform1(pathProgram,"worldGrid",0); Uniform1(pathProgram,"wallTex",1); Uniform1(pathProgram,"floorTex",2); Uniform1(pathProgram,"ceilingTex",3);
        Uniform1(pathProgram,"fixtureMap",4);
        Uniform1(pathProgram,"faceTex",5);
        Uniform1(pathProgram,"eyeTex",6);
        Uniform1(pathProgram,"probeDirectTex",7);
        Uniform1(pathProgram,"probeBounceTex",8);
        MonsterState? monster=visibleMonster;
        Gl.Uniform4fv(Gl.GetUniformLocation(pathProgram,"monsterData"),1,
            [menu.MonsterSettings.Enabled ? monster?.X??-100f : -100f,
             monster?.Y??1.5f,monster?.Z??-100f,monster?.Yaw??0f]);
        Uniform1(pathProgram,"frameIndex",frames); Uniform1(pathProgram,"lightsEnabled",menu.Lights?1:0); Uniform1(pathProgram,"worldSeed",seed);
        Uniform1(pathProgram,"hybridMode",menu.RenderMode==1?1:0);
        Uniform1(pathProgram,"fixedPathMode",menu.RenderMode==2?1:0);
        Uniform1(pathProgram,"probeSize",probeSize);
        Uniform1(pathProgram,"hybridDirectSamples",menu.HybridDirectSamples);
        Uniform1(pathProgram,"sampleCount",menu.Samples);
        Uniform1(pathProgram,"bounceCount",menu.Bounces);
        Uniform1f(pathProgram,"bounceStrength",menu.BounceStrength);
        Uniform1f(pathProgram,"aoStrength",menu.AO);
        Uniform1f(pathProgram,"lightDensity",menu.LightDensity);
        Uniform1f(pathProgram,"lightIntensity",menu.LightIntensity);
        Uniform1(pathProgram,"volumetricsEnabled",menu.Volumetrics?1:0);
        float chaseFog=menu.FlickerSettings.ChaseFog ? chaseBlend : 0f;
        Uniform1f(pathProgram,"fogDensity",menu.FogDensity+
            (MathF.Max(menu.FogDensity,menu.FlickerSettings.ChaseFogMax)-menu.FogDensity)*chaseFog);
        Uniform1(pathProgram,"fogSteps",menu.FogSteps);
        UploadObjects(pathProgram,true);
        int remoteCount=Math.Min(4,remotePositions.Count);
        Uniform1(pathProgram,"remoteCount",remoteCount);
        if(remoteCount>0)
        {
            float[] remoteData=new float[remoteCount*4];
            float[] colors=new float[remoteCount*3];
            int i=0;
            foreach(var player in remotePositions.Take(remoteCount))
            {
                int j=i++*4;
                remoteData[j]=player.Value.X;remoteData[j+1]=player.Value.Y;
                remoteData[j+2]=player.Value.Z;remoteData[j+3]=remoteYaws[player.Key];
                Vector3 color=remoteColors.TryGetValue(player.Key,out Vector3 chosen)?chosen:new Vector3(0.78f,0.76f,0.70f);
                int k=(i-1)*3;colors[k]=color.X;colors[k+1]=color.Y;colors[k+2]=color.Z;
            }
            Gl.Uniform4fv(Gl.GetUniformLocation(pathProgram,"remoteData[0]"),remoteCount,remoteData);
            Gl.Uniform3fv(Gl.GetUniformLocation(pathProgram,"remoteColor[0]"),remoteCount,colors);
        }
        Uniform2(pathProgram,"resolution",width,height);
        ViewPose(out Vector3 viewPosition,out Vector3 forward);
        Gl.Uniform3f(Gl.GetUniformLocation(pathProgram,"cameraPos"),viewPosition.X,viewPosition.Y,viewPosition.Z);
        float viewYaw=smoothYaw+motion.YawOffset;
        float viewPitch=Math.Clamp(smoothPitch+motion.PitchOffset,-1.45f,1.45f);
        Vector3 right=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,forward));
        Vector3 up=Vector3.Normalize(Vector3.Cross(forward,right));
        float rollCos=MathF.Cos(motion.Roll), rollSin=MathF.Sin(motion.Roll);
        Vector3 rolledRight=right*rollCos+up*rollSin;
        Vector3 rolledUp=up*rollCos-right*rollSin;
        float[] basis={rolledRight.X,rolledRight.Y,rolledRight.Z,rolledUp.X,rolledUp.Y,rolledUp.Z,forward.X,forward.Y,forward.Z};
        Gl.UniformMatrix3fv(Gl.GetUniformLocation(pathProgram,"cameraBasis"),1,false,basis);
        PrepareMonsterOverlay(viewPosition,rolledRight,rolledUp,forward);
        PreparePlayerTags(viewPosition,rolledRight,rolledUp,forward);
        PrepareObjectFrames(viewPosition,rolledRight,rolledUp,forward);
        Gl.DrawArrays(0x0004,0,3);
        sceneTexture=renderTexture;
        RunPostProcessing();
        if(menu.Dirty) UploadOverlay();
        Gl.Viewport(0,0,FramebufferSize.Width,FramebufferSize.Height);
        Gl.UseProgram(displayProgram);
        Bind(0,sceneTexture); Uniform1(displayProgram,"accumulation",0);
        Bind(1,overlayTexture); Uniform1(displayProgram,"overlayTexture",1);
        Bind(2,bloomA);Uniform1(displayProgram,"bloomTexture",2);
        Bind(3,exposureCurrent==0?exposureA:exposureB);Uniform1(displayProgram,"exposureTexture",3);
        Uniform1(displayProgram,"ditheringEnabled",menu.Dithering?1:0);
        Uniform1(displayProgram,"paletteReduction",menu.PaletteReduction?1:0);
        Uniform1(displayProgram,"temperature",menu.Temperature);
        Uniform1(displayProgram,"paletteSteps",menu.PaletteSteps);
        (float transitionDark,float transitionFlash)=spawnTransition.Sample(watch.Elapsed.TotalSeconds);
        Uniform1f(displayProgram,"transitionDark",transitionDark);
        Uniform1f(displayProgram,"transitionFlash",transitionFlash);
        Uniform1(displayProgram,"autoExposure",menu.AutoExposure?1:0);
        Uniform1(displayProgram,"postEnabled",menu.PostEnabled?1:0);
        Uniform1(displayProgram,"bloomEnabled",menu.Bloom?1:0);
        Uniform1f(displayProgram,"bloomStrength",menu.BloomStrength);
        Uniform1(displayProgram,"tonemapMode",menu.TonemapMode);
        Uniform1f(displayProgram,"exposure",menu.Exposure);
        Uniform1f(displayProgram,"saturation",menu.Saturation);
        Uniform1f(displayProgram,"contrast",menu.Contrast);
        Gl.Uniform3f(Gl.GetUniformLocation(displayProgram,"colorMix"),menu.RedMix,menu.GreenMix,menu.BlueMix);
        Uniform2(displayProgram,"windowSize",FramebufferSize.Width,FramebufferSize.Height);
        Gl.DrawArrays(0x0004,0,3);
        frames++;
        reportFrames++;
        if (watch.Elapsed.TotalSeconds-reportTime>0.5)
        {
            double elapsed=watch.Elapsed.TotalSeconds-reportTime;
            fpsValue=(int)Math.Round(reportFrames/elapsed);
            frameMs=(float)(elapsed*1000.0/Math.Max(reportFrames,1));
            menu.FpsValue=fpsValue;
            Text="BEANROOMS";
            reportFrames=0; reportTime=watch.Elapsed.TotalSeconds;
        }
        if(watch.Elapsed.TotalSeconds-lastDebugTime>=0.2)
        {
            lastDebugTime=watch.Elapsed.TotalSeconds;
            menu.DebugSeed=seed;
            menu.DebugLines=
            [
                $"FPS {fpsValue:000}  {frameMs:0.0}MS",
                $"{menu.RenderModeName.ToUpperInvariant()} {width}X{height}",
                $"SPP {menu.Samples}  GI {menu.Bounces}",
                $"SEED {seed}",
                $"XYZ {camera.X:0.0} {camera.Y:0.0} {camera.Z:0.0}",
                network.Connected?$"PHOTON {remotePositions.Count+1}P":"SOLO",
                $"CUBES {menu.BodyCount}",
                $"EYE {(menu.MonsterSettings.Enabled ? visibleMonster?.Mode switch { 2=>"CHASE",1=>"SEARCH",_=>"ROAM" } : "OFF")}",
                $"VECTORS {(menu.MonsterDebug?"ON":"OFF")}",
                $"FOG {(menu.Volumetrics?"ON":"OFF")} {menu.FogSteps}STEP"
            ];
            if(menu.ShowDebugOverlay||menu.Open) menu.Dirty=true;
        }
    }
}
