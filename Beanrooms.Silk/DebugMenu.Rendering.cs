using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    static void PixelText(Graphics g, string text, Brush brush, int x, int y)
    {
        DrawPixelText(g,text,Brushes.Black,x+1,y+1);
        DrawPixelText(g,text,brush,x,y);
    }
    static void DrawPixelText(Graphics g, string text, Brush brush, int x, int y)
    {
        foreach(char c in text.ToUpperInvariant())
        {
            if(Glyphs.TryGetValue(c,out var rows))
                for(int yy=0;yy<7;yy++) for(int xx=0;xx<5;xx++)
                    if((rows[yy] & (1 << (4-xx))) != 0) g.FillRectangle(brush,x+xx,y+yy,1,1);
            x += 6;
        }
    }

    static void DrawFrameCorners(Graphics g,Brush brush,RectangleF bounds,int offset)
    {
        int left=(int)bounds.Left+offset,top=(int)bounds.Top+offset;
        int right=(int)bounds.Right+offset,bottom=(int)bounds.Bottom+offset;
        int corner=Math.Clamp(Math.Min(right-left,bottom-top)/4,2,4);
        g.FillRectangle(brush,left,top,corner,1);
        g.FillRectangle(brush,left,top,1,corner);
        g.FillRectangle(brush,right-corner,top,corner,1);
        g.FillRectangle(brush,right-1,top,1,corner);
        g.FillRectangle(brush,left,bottom-1,corner,1);
        g.FillRectangle(brush,left,bottom-corner,1,corner);
        g.FillRectangle(brush,right-corner,bottom-1,corner,1);
        g.FillRectangle(brush,right-1,bottom-corner,1,corner);
    }
    internal Bitmap Draw()
    {
        var image = new Bitmap(320, 240, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.CompositingMode = CompositingMode.SourceOver;
        g.SmoothingMode = SmoothingMode.None;
        g.Clear(Color.Transparent);
        using var white = new SolidBrush(Color.FromArgb(210, 210, 210));
        using var paleBlue = new SolidBrush(Color.FromArgb(175, 175, 175));
        using var green = new SolidBrush(Color.White);
        if(Open) DrawMenu(g);
        else
        {
            if(ShowFps) PixelText(g,$"FPS {Math.Clamp(FpsValue,0,999):000}",white,13,10);
            if(ShowPlayerTags) foreach(var tag in PlayerTags)
            {
                using var brush=new SolidBrush(tag.Ink);
                PixelText(g,tag.Label,brush,(int)tag.At.X-tag.Label.Length*3,(int)tag.At.Y);
            }
            if(ShowObjectFrames) foreach(var frame in ObjectFrames)
            {
                DrawFrameCorners(g,Brushes.Black,frame,1);
                DrawFrameCorners(g,white,frame,0);
                if(ObjectPrompt.Length>0)
                    PixelText(g,ObjectPrompt,white,Math.Clamp((int)frame.Left,5,315-ObjectPrompt.Length*6),
                        Math.Clamp((int)frame.Bottom+3,10,211));
            }
            if(ShowDebugOverlay)
            {
                for(int line=0;line<DebugLines.Length;line++)
                    PixelText(g,DebugLines[line],line==0?green:line==1?paleBlue:white,13,10+line*12);
                PixelText(g,MonsterTargetText,paleBlue,190,10);
            }
            if(ShowDebugOverlay && MonsterDebug)
            {
                foreach(var segment in MonsterSegments)
                {
                    using var pen=new Pen(segment.Ink);
                    g.DrawLine(pen,segment.A,segment.B);
                }
                foreach(var point in MonsterPoints)
                {
                    using var brush=new SolidBrush(point.Ink);
                    g.FillRectangle(brush,point.At.X-2,point.At.Y-2,5,5);
                    PixelText(g,point.Label,brush,(int)point.At.X+4,(int)point.At.Y-3);
                }
            }
        }
        Dirty = false;
        return image;
    }
}
