using System.Numerics;

namespace Beanrooms;

internal enum SandboxKind { Cube, GlowCube, Thruster, Button, Lever, And, Or, Not, Lamp, Xor, Nand, Xnor, Toggle, Timer, Piston }

internal sealed class SandboxDevice
{
    public int Id { get; set; }
    public SandboxKind Kind { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Qx { get; set; }
    public float Qy { get; set; }
    public float Qz { get; set; }
    public float Qw { get; set; } = 1f;
    public int BodyId { get; set; }
    public bool State { get; set; }
    public float PressTime { get; set; }
    // Toggle latches on a rising input edge; Timer free-runs as a clock source.
    public bool PrevInput { get; set; }
    public float Timer { get; set; }
    public Vector3 Position => new(X,Y,Z);
    public Quaternion Rotation => new(Qx,Qy,Qz,Qw);
}

internal sealed class SandboxWire
{
    public int From { get; set; }
    public int To { get; set; }
}

internal sealed class SandboxWorld
{
    internal readonly List<SandboxDevice> Devices = [];
    internal readonly List<SandboxWire> Wires = [];
    int nextId;

    internal void Clear() { Devices.Clear(); Wires.Clear(); nextId=0; }
    internal void Remove(int id)
    {
        Devices.RemoveAll(device=>device.Id==id);
        Wires.RemoveAll(wire=>wire.From==id||wire.To==id);
    }

    internal SandboxDevice? Spawn(SandboxKind kind,Vector3 eye,Vector3 forward,float yaw,
        IReadOnlyList<System.Drawing.RectangleF> walls)
    {
        if(kind is SandboxKind.Cube or SandboxKind.GlowCube || Devices.Count>=64) return null;
        Vector3 horizontal=new(forward.X,0f,forward.Z);
        if(horizontal.LengthSquared()<.001f) horizontal=Vector3.UnitZ;
        horizontal=Vector3.Normalize(horizontal);
        Vector3 right=new(horizontal.Z,0f,-horizontal.X);
        Vector2[] offsets=[Vector2.Zero,new(1,0),new(-1,0),new(0,1),new(0,-1),
            new(2,0),new(-2,0),new(1,1),new(-1,1),new(1,-1),new(-1,-1)];
        Vector3 at=default;
        bool found=false;
        foreach(Vector2 offset in offsets)
        {
            Vector3 candidate=eye+horizontal*(2.5f+offset.Y*.9f)+right*(offset.X*.9f);
            candidate.Y=.36f;
            if(candidate.X<4.3f || candidate.X>187.7f || candidate.Z<4.3f || candidate.Z>187.7f ||
               walls.Any(w=>candidate.X>w.Left-.45f && candidate.X<w.Right+.45f &&
                            candidate.Z>w.Top-.45f && candidate.Z<w.Bottom+.45f) ||
               Devices.Any(device=>Vector3.DistanceSquared(device.Position,candidate)<.8f*.8f)) continue;
            at=candidate;found=true;break;
        }
        if(!found) return null;
        Quaternion rotation=Quaternion.CreateFromAxisAngle(Vector3.UnitY,yaw);
        var device=new SandboxDevice { Id=++nextId,Kind=kind,X=at.X,Y=at.Y,Z=at.Z,Yaw=yaw,
            Qx=rotation.X,Qy=rotation.Y,Qz=rotation.Z,Qw=rotation.W };
        Devices.Add(device);
        return device;
    }

    internal SandboxDevice? Aim(Vector3 eye,Vector3 forward,float range=6f)
    {
        SandboxDevice? best=null;
        float distance=range;
        foreach(SandboxDevice device in Devices)
        {
            Vector3 delta=device.Position-eye;
            float along=Vector3.Dot(delta,forward);
            if(along<0 || along>distance || Vector3.DistanceSquared(delta,forward*along)>0.42f*0.42f) continue;
            best=device;distance=along;
        }
        return best;
    }

    internal bool Connect(int from,int to)
    {
        if(from==to || Wires.Count>=128 || Wires.Any(w=>w.From==from && w.To==to)) return false;
        SandboxDevice? source=Devices.FirstOrDefault(d=>d.Id==from);
        SandboxDevice? target=Devices.FirstOrDefault(d=>d.Id==to);
        if(source==null || target==null || source.Kind is SandboxKind.Thruster or SandboxKind.Lamp or SandboxKind.Piston ||
           target.Kind is SandboxKind.Button or SandboxKind.Lever) return false;
        if(Vector3.DistanceSquared(source.Position,target.Position)>20f*20f) return false;
        Wires.Add(new SandboxWire { From=from,To=to });
        return true;
    }

    internal void Interact(int id)
    {
        SandboxDevice? device=Devices.FirstOrDefault(d=>d.Id==id);
        if(device?.Kind==SandboxKind.Button) { device.State=true;device.PressTime=0.25f; }
        else if(device?.Kind==SandboxKind.Lever) device.State=!device.State;
    }

    internal void Update(float dt,Action<SandboxDevice>? poweredThruster=null,Action<SandboxDevice>? poweredPiston=null)
    {
        foreach(SandboxDevice device in Devices)
            if(device.Kind==SandboxKind.Button && device.PressTime>0f)
            { device.PressTime=Math.Max(0f,device.PressTime-dt);device.State=device.PressTime>0f; }
        // Stateful sources settle once per frame before the combinational pass runs.
        foreach(SandboxDevice device in Devices)
        {
            int inputs=0,active=0;
            foreach(SandboxWire wire in Wires)
                if(wire.To==device.Id)
                { inputs++;if(Devices.FirstOrDefault(d=>d.Id==wire.From)?.State==true) active++; }
            if(device.Kind==SandboxKind.Toggle)
            {
                bool high=inputs>0 && active>0;
                if(high && !device.PrevInput) device.State=!device.State;
                device.PrevInput=high;
            }
            else if(device.Kind==SandboxKind.Timer)
            {
                device.Timer+=dt;
                if(device.Timer>=0.5f) { device.Timer-=0.5f;device.State=!device.State; }
            }
        }
        // Iterate enough times for short gate chains. Feedback loops settle at the previous state.
        for(int pass=0;pass<8;pass++)
        {
            bool changed=false;
            foreach(SandboxDevice device in Devices)
            {
                if(device.Kind is SandboxKind.Button or SandboxKind.Lever or SandboxKind.Toggle or SandboxKind.Timer) continue;
                int inputs=0,active=0;
                foreach(SandboxWire wire in Wires)
                    if(wire.To==device.Id)
                    { inputs++;if(Devices.FirstOrDefault(d=>d.Id==wire.From)?.State==true) active++; }
                bool value=device.Kind switch
                {
                    SandboxKind.And => inputs>=2 && active==inputs,
                    SandboxKind.Or => active>0,
                    SandboxKind.Not => inputs>0 && active==0,
                    SandboxKind.Xor => inputs>0 && (active&1)==1,
                    SandboxKind.Nand => inputs>=2 && active<inputs,
                    SandboxKind.Xnor => inputs>=2 && (active&1)==0,
                    _ => active>0
                };
                if(device.State!=value) { device.State=value;changed=true; }
            }
            if(!changed) break;
        }
        if(poweredThruster!=null) foreach(SandboxDevice device in Devices)
            if(device.Kind==SandboxKind.Thruster && device.State) poweredThruster(device);
        if(poweredPiston!=null) foreach(SandboxDevice device in Devices)
            if(device.Kind==SandboxKind.Piston && device.State) poweredPiston(device);
    }
}
