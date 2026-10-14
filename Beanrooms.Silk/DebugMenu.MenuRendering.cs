using System.Drawing;

namespace Beanrooms;

internal sealed partial class DebugMenu
{
    // Menus use the same shadowed 5x7 glyphs as the diagnostics overlay.
    void DrawMenu(Graphics g)
    {
        using var white=new SolidBrush(Color.White);
        DrawTerminal(g,white);
    }
}
