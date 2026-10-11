using System.Drawing;
using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Rectangle = System.Drawing.Rectangle;

namespace Beanrooms;

internal enum Keys
{
    None,A,B,Back,C,ControlKey,D,D1,D2,D3,Delete,Down,E,Enter,Escape,F,F11,G,H,
    K,LControlKey,Left,LShiftKey,M,OemCloseBrackets,OemMinus,OemOpenBrackets,Oemplus,
    P,Q,R,RControlKey,Right,RShiftKey,S,ShiftKey,Space,T,Tab,Up,V,W
}
internal sealed class KeyEventArgs(Keys key,bool control) : EventArgs
{
    internal Keys KeyCode { get; }=key;
    internal bool Control { get; }=control;
    internal bool Handled { get; set; }
}
internal sealed class KeyPressEventArgs(char value) : EventArgs
{
    internal char KeyChar { get; }=value;
    internal bool Handled { get; set; }
}
internal enum MouseButtons { Left,Right,Middle }
internal sealed class MouseEventArgs(Point location,MouseButtons button=MouseButtons.Left,int delta=0) : EventArgs
{
    internal Point Location { get; }=location;
    internal MouseButtons Button { get; }=button;
    internal int Delta { get; }=delta;
}
internal struct Message { }

// A narrow facade for the old game loop. Silk/GLFW owns the actual OS window,
// event pump, OpenGL context and swap chain on both Windows and Linux.
internal abstract class SilkForm : IDisposable
{
    readonly IWindow window;
    IInputContext? input;
    IMouse? mouse;
    Vector2 previousMouse;
    bool haveMouse;
    readonly HashSet<Keys> held=new();
    Size initialSize=new(960,720);
    Point initialPosition;
    bool positionSet;
    bool disposed;
    bool focused=true;
    internal event EventHandler<KeyEventArgs>? KeyDown,KeyUp;
    internal event EventHandler<KeyPressEventArgs>? KeyPress;
    internal event EventHandler<MouseEventArgs>? MouseDown,MouseUp,MouseMove,MouseWheel;
    internal event EventHandler? Activated,Deactivate,FormClosed;

    protected SilkForm()
    {
        var options=WindowOptions.Default;
        options.Size=new Vector2D<int>(960,720);
        options.Title="BEANROOMS";
        options.API=new GraphicsAPI(ContextAPI.OpenGL,ContextProfile.Core,ContextFlags.Default,new APIVersion(4,3));
        options.VSync=false;
        options.WindowBorder=WindowBorder.Fixed;
        options.ShouldSwapAutomatically=true;
        window=Window.Create(options);
        window.Load += Load;
        window.Render += _ => OnFrame();
        window.Closing += Closing;
        window.FocusChanged += value => { focused=value;if(value) Activated?.Invoke(this,EventArgs.Empty);else Deactivate?.Invoke(this,EventArgs.Empty); };
    }
    internal Size ClientSize { get => window.IsInitialized ? new Size(window.Size.X,window.Size.Y) : initialSize;
        set { initialSize=value;if(window.IsInitialized) window.Size=new Vector2D<int>(value.Width,value.Height); } }
    internal Size FramebufferSize => window.IsInitialized ? new Size(window.FramebufferSize.X,window.FramebufferSize.Y) : ClientSize;
    internal Point Location { get => window.IsInitialized ? new Point(window.Position.X,window.Position.Y) : initialPosition;
        set { initialPosition=value;positionSet=true;if(window.IsInitialized) window.Position=new Vector2D<int>(value.X,value.Y); } }
    internal Rectangle Bounds { get => new(Location,ClientSize);
        set { Location=value.Location;ClientSize=value.Size; } }
    internal bool Fullscreen
    {
        get => window.WindowState==WindowState.Fullscreen;
        set
        {
            if(value)
            {
                window.WindowState=WindowState.Fullscreen;
            }
            else window.WindowState=WindowState.Normal;
        }
    }
    internal string Text { get => window.Title;set => window.Title=value; }
    internal bool Focused => focused;
    internal bool IsDisposed => disposed;
    internal string ClipboardText => input?.Keyboards.FirstOrDefault()?.ClipboardText ?? "";
    internal Rectangle DesktopBounds
    {
        get
        {
            var bounds=window.IsInitialized ? Silk.NET.Windowing.Monitor.GetMainMonitor(window)?.Bounds : null;
            return bounds.HasValue ? new Rectangle(bounds.Value.Origin.X,bounds.Value.Origin.Y,bounds.Value.Size.X,bounds.Value.Size.Y)
                : new Rectangle(0,0,1920,1080);
        }
    }
    internal IntPtr GetGlAddress(string name) => window.GLContext!.GetProcAddress(name);
    internal void SetVsync(bool enabled) => window.VSync=enabled;
    internal void Run() => window.Run();
    internal void Close() => window.Close();
    internal void BeginInvoke(Action action) => action();
    internal void CaptureCursor()
    {
        if(mouse==null) return;
        mouse.Cursor.CursorMode=CursorMode.Raw;
        haveMouse=false;
    }
    internal void ReleaseCursor()
    {
        if(mouse==null) return;
        mouse.Cursor.CursorMode=CursorMode.Normal;
        haveMouse=false;
    }
    void Load()
    {
        window.Size=new Vector2D<int>(initialSize.Width,initialSize.Height);
        if(positionSet) window.Position=new Vector2D<int>(initialPosition.X,initialPosition.Y);
        input=window.CreateInput();
        foreach(var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (kb,key,scanCode) =>
            {
                var translated=Translate(key);
                if(translated==Keys.None) return;
                bool repeated=!held.Add(translated);
                if((translated==Keys.Tab||translated==Keys.F11) && !repeated)
                {
                    var msg=new Message();
                    if(ProcessCmdKey(ref msg,translated)) return;
                }
                KeyDown?.Invoke(this,new KeyEventArgs(translated,HeldControl()));
            };
            keyboard.KeyUp += (kb,key,_) =>
            {
                var translated=Translate(key);
                held.Remove(translated);
                if(translated!=Keys.None) KeyUp?.Invoke(this,new KeyEventArgs(translated,HeldControl()));
            };
            keyboard.KeyChar += (_,character) => KeyPress?.Invoke(this,new KeyPressEventArgs(character));
        }
        if(input.Mice.Count>0)
        {
            mouse=input.Mice[0];
            mouse.MouseMove += (_,position) =>
            {
                if(haveMouse) OnRawMouse(position.X-previousMouse.X,position.Y-previousMouse.Y);
                previousMouse=position;haveMouse=true;
                MouseMove?.Invoke(this,new MouseEventArgs(new Point((int)position.X,(int)position.Y)));
            };
            mouse.MouseDown += (_,button) => MouseDown?.Invoke(this,new MouseEventArgs(MousePoint(),Translate(button)));
            mouse.MouseUp += (_,button) => MouseUp?.Invoke(this,new MouseEventArgs(MousePoint(),Translate(button)));
            mouse.Scroll += (_,wheel) => MouseWheel?.Invoke(this,new MouseEventArgs(MousePoint(),delta:(int)MathF.Round(wheel.Y*120)));
        }
        OnShown(EventArgs.Empty);
    }
    Point MousePoint() => new((int)previousMouse.X,(int)previousMouse.Y);
    bool HeldControl() => held.Contains(Keys.ControlKey)||held.Contains(Keys.LControlKey)||held.Contains(Keys.RControlKey);
    static MouseButtons Translate(Silk.NET.Input.MouseButton value) => value switch
    {
        Silk.NET.Input.MouseButton.Right => MouseButtons.Right,
        Silk.NET.Input.MouseButton.Middle => MouseButtons.Middle,
        _ => MouseButtons.Left
    };
    static Keys Translate(Key key) => key switch
    {
        Key.A=>Keys.A,Key.B=>Keys.B,Key.C=>Keys.C,Key.D=>Keys.D,Key.E=>Keys.E,
        Key.F=>Keys.F,Key.G=>Keys.G,Key.H=>Keys.H,Key.K=>Keys.K,Key.M=>Keys.M,Key.P=>Keys.P,Key.Q=>Keys.Q,
        Key.R=>Keys.R,Key.S=>Keys.S,Key.T=>Keys.T,Key.V=>Keys.V,Key.W=>Keys.W,
        Key.Number1=>Keys.D1,Key.Number2=>Keys.D2,Key.Number3=>Keys.D3,
        Key.Backspace=>Keys.Back,Key.Delete=>Keys.Delete,Key.Down=>Keys.Down,
        Key.Enter=>Keys.Enter,Key.Escape=>Keys.Escape,Key.F11=>Keys.F11,
        Key.Left=>Keys.Left,Key.Right=>Keys.Right,Key.Up=>Keys.Up,
        Key.Space=>Keys.Space,Key.Tab=>Keys.Tab,Key.ControlLeft=>Keys.LControlKey,
        Key.ControlRight=>Keys.RControlKey,Key.ShiftLeft=>Keys.LShiftKey,
        Key.ShiftRight=>Keys.RShiftKey,Key.LeftBracket=>Keys.OemOpenBrackets,
        Key.RightBracket=>Keys.OemCloseBrackets,Key.Minus=>Keys.OemMinus,
        Key.Equal=>Keys.Oemplus,_=>Keys.None
    };
    void Closing()
    {
        if(disposed) return;
        disposed=true;
        FormClosed?.Invoke(this,EventArgs.Empty);
        input?.Dispose();
    }
    protected virtual void OnShown(EventArgs e) { }
    protected virtual bool ProcessCmdKey(ref Message message,Keys key) => false;
    protected virtual void OnRawMouse(float dx,float dy) { }
    protected abstract void OnFrame();
    public void Dispose() { if(!disposed) Closing();window.Dispose(); }
}
