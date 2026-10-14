using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    // Menus use the same shadowed 5x7 glyphs as the diagnostics overlay.
    void DrawMenu(Graphics g)
    {
        bool inSession=!TitleScreen && !SettingsFromTitle;
        using var background=new SolidBrush(Color.FromArgb(inSession?225:255,0,0,0));
        using var white=new SolidBrush(Color.White);
        g.FillRectangle(background,0,0,320,240);
        DrawTerminal(g,white);
    }
}
