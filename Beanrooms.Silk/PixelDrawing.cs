using System.Drawing;
using System.Runtime.InteropServices;
using StbImageSharp;

namespace Beanrooms;

// The menu glyphs are drawn into a 320x240 BGRA buffer. This is deliberately a
// small software rasterizer: it keeps the original pixel artwork without GDI+.
internal enum PixelFormat { Format32bppArgb }
internal enum ImageLockMode { ReadOnly }
internal enum CompositingMode { SourceOver }
internal enum SmoothingMode { None }
internal sealed class BitmapData { internal GCHandle Handle; internal IntPtr Scan0 => Handle.AddrOfPinnedObject(); }
internal sealed class Bitmap : IDisposable
{
    internal readonly byte[] Pixels;
    internal int Width { get; }
    internal int Height { get; }
    internal Bitmap(int width,int height,PixelFormat format)
    {
        Width=width;Height=height;Pixels=new byte[width*height*4];
    }
    internal Bitmap(string path)
    {
        var image=ImageResult.FromMemory(File.ReadAllBytes(path),ColorComponents.RedGreenBlueAlpha);
        Width=image.Width;Height=image.Height;Pixels=image.Data;
        for(int i=0;i<Pixels.Length;i+=4) (Pixels[i],Pixels[i+2])=(Pixels[i+2],Pixels[i]);
    }
    internal BitmapData LockBits(Rectangle bounds,ImageLockMode mode,PixelFormat format)
        => new() { Handle=GCHandle.Alloc(Pixels,GCHandleType.Pinned) };
    internal void UnlockBits(BitmapData data) => data.Handle.Free();
    public void Dispose() { }
}
internal class Brush : IDisposable
{
    internal readonly Color Ink;
    internal Brush(Color ink) => Ink=ink;
    public void Dispose() { }
}
internal sealed class SolidBrush(Color ink) : Brush(ink);
internal static class Brushes { internal static readonly Brush Black=new SolidBrush(Color.Black); }
internal sealed class Pen(Color ink) : IDisposable
{
    internal Color Ink { get; }=ink;
    public void Dispose() { }
}
internal sealed class Graphics(Bitmap target) : IDisposable
{
    internal CompositingMode CompositingMode { get; set; }
    internal SmoothingMode SmoothingMode { get; set; }
    internal static Graphics FromImage(Bitmap image) => new(image);
    internal void Clear(Color color)
    {
        for(int i=0;i<target.Pixels.Length;i+=4) Write(i,color);
    }
    internal void DrawImageUnscaled(Bitmap image,int x,int y)
    {
        for(int row=0;row<image.Height;row++)
            if(row+y>=0 && row+y<target.Height)
                Buffer.BlockCopy(image.Pixels,row*image.Width*4,target.Pixels,((row+y)*target.Width+x)*4,Math.Min(image.Width,target.Width-x)*4);
    }
    void Write(int index,Color c)
    {
        target.Pixels[index]=c.B; target.Pixels[index+1]=c.G;
        target.Pixels[index+2]=c.R; target.Pixels[index+3]=c.A;
    }
    void Plot(int x,int y,Color c)
    {
        if((uint)x>=(uint)target.Width||(uint)y>=(uint)target.Height) return;
        int i=(y*target.Width+x)*4;
        if(c.A==255) { Write(i,c);return; }
        if(c.A==0) return;
        int a=c.A,under=255-a;
        target.Pixels[i]=(byte)((c.B*a+target.Pixels[i]*under+127)/255);
        target.Pixels[i+1]=(byte)((c.G*a+target.Pixels[i+1]*under+127)/255);
        target.Pixels[i+2]=(byte)((c.R*a+target.Pixels[i+2]*under+127)/255);
        target.Pixels[i+3]=(byte)(a+(target.Pixels[i+3]*under+127)/255);
    }
    internal void FillRectangle(Brush brush,int x,int y,int width,int height)
    {
        for(int yy=Math.Max(0,y);yy<Math.Min(target.Height,y+height);yy++)
            for(int xx=Math.Max(0,x);xx<Math.Min(target.Width,x+width);xx++) Plot(xx,yy,brush.Ink);
    }
    internal void FillRectangle(Brush brush,float x,float y,float width,float height)
        => FillRectangle(brush,(int)MathF.Round(x),(int)MathF.Round(y),(int)MathF.Round(width),(int)MathF.Round(height));
    internal void DrawLine(Pen pen,PointF a,PointF b)
    {
        int x0=(int)MathF.Round(a.X),y0=(int)MathF.Round(a.Y);
        int x1=(int)MathF.Round(b.X),y1=(int)MathF.Round(b.Y);
        int dx=Math.Abs(x1-x0),dy=-Math.Abs(y1-y0),sx=x0<x1?1:-1,sy=y0<y1?1:-1,err=dx+dy;
        while(true)
        {
            Plot(x0,y0,pen.Ink);
            if(x0==x1&&y0==y1) break;
            int e=err*2;
            if(e>=dy) { err+=dy;x0+=sx; }
            if(e<=dx) { err+=dx;y0+=sy; }
        }
    }
    public void Dispose() { }
}
