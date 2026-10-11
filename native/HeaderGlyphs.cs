using System;
using System.Runtime.InteropServices;

namespace ProxyTrafficFloat.Native;

/// <summary>
/// Heroicons toolbar glyphs rendered with Windows GDI+.
/// Brushes, flattened paths and the transform are retained for the canvas lifetime.
/// </summary>
public sealed class HeaderGlyphs : IDisposable
{
    readonly IntPtr[] paths = new IntPtr[6];
    IntPtr graphics, matrix, softInk, hoverInk, lockedInk, hoverBackground;
    nuint token;

    public HeaderGlyphs()
    {
        var startup = new StartupInput { Version = 1 };
        Check(GdiplusStartup(out token, ref startup, IntPtr.Zero));
        try
        {
            Check(GdipCreateMatrix(out matrix));
            Check(GdipCreateSolidFill(0xFFA7B4C8, out softInk));
            Check(GdipCreateSolidFill(0xFFE5ECF7, out hoverInk));
            Check(GdipCreateSolidFill(0xFF62D8BD, out lockedInk));
            Check(GdipCreateSolidFill(0x80344055, out hoverBackground));
            GlyphPaths.Load(paths);
            Check(GdipCreatePath(0, out paths[5]));
            RoundedRect(paths[5], 0, 0, 22, 22, 6);
        }
        catch { Dispose(); throw; }
    }

    static void RoundedRect(IntPtr path, float x, float y, float width, float height, float radius)
    {
        const float bezier = 0.55228475f;
        float right = x + width, bottom = y + height, control = radius * bezier;
        GdipStartPathFigure(path);
        GdipAddPathLine(path, x + radius, y, right - radius, y);
        GdipAddPathBezier(path, right - radius, y, right - radius + control, y,
            right, y + radius - control, right, y + radius);
        GdipAddPathLine(path, right, y + radius, right, bottom - radius);
        GdipAddPathBezier(path, right, bottom - radius, right, bottom - radius + control,
            right - radius + control, bottom, right - radius, bottom);
        GdipAddPathLine(path, right - radius, bottom, x + radius, bottom);
        GdipAddPathBezier(path, x + radius, bottom, x + radius - control, bottom,
            x, bottom - radius + control, x, bottom - radius);
        GdipAddPathLine(path, x, bottom - radius, x, y + radius);
        GdipAddPathBezier(path, x, y + radius, x, y + radius - control, x + radius - control, y, x + radius, y);
        GdipClosePathFigure(path);
    }

    public void Bind(IntPtr dc)
    {
        Unbind();
        Check(GdipCreateFromHDC(dc, out graphics));
        GdipSetPageUnit(graphics, 2);
        GdipSetSmoothingMode(graphics, 4);
        GdipSetPixelOffsetMode(graphics, 4);
        GdipSetCompositingQuality(graphics, 2);
    }

    public void Unbind()
    {
        if (graphics == IntPtr.Zero) return;
        GdipDeleteGraphics(graphics);
        graphics = IntPtr.Zero;
    }

    public void Draw(double scale, int logicalWidth, bool mini, bool locked, int hovered)
    {
        if (mini)
            DrawButton(scale, logicalWidth - 24, 19, 2, locked, hovered);
        else
        {
            DrawButton(scale, logicalWidth - 90, 4, 0, locked, hovered);
            DrawButton(scale, logicalWidth - 68, 4, 1, locked, hovered);
            DrawButton(scale, logicalWidth - 46, 4, 2, locked, hovered);
            DrawButton(scale, logicalWidth - 24, 4, 3, locked, hovered);
        }
        // Finish GDI+ drawing before ordinary GDI text or bitmap copying resumes.
        GdipFlush(graphics, 1);
    }

    void DrawButton(double scale, double x, double y, int button, bool locked, int hovered)
    {
        float s = (float)scale;
        bool isHovered = hovered == button;
        if (isHovered)
        {
            GdipSetMatrixElements(matrix, s, 0, 0, s, (float)(x * scale), (float)(y * scale));
            GdipSetWorldTransform(graphics, matrix);
            GdipFillPath(graphics, hoverBackground, paths[5]);
        }

        // The original 20-unit viewbox becomes a 14-DIP glyph, centered inside its 22-DIP tile.
        float glyphScale = 0.70f * s;
        GdipSetMatrixElements(matrix, glyphScale, 0, 0, glyphScale,
            (float)((x + 4) * scale), (float)((y + 4) * scale));
        GdipSetWorldTransform(graphics, matrix);
        IntPtr ink = button == 2 && locked ? lockedInk : isHovered ? hoverInk : softInk;
        int symbol = button switch { 0 => 0, 1 => 1, 2 => locked ? 2 : 3, _ => 4 };
        GdipFillPath(graphics, ink, paths[symbol]);
    }

    static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException("无法初始化原生按钮绘制：" + status);
    }

    public void Dispose()
    {
        if (token == 0) return;
        Unbind();
        foreach (var path in paths) if (path != IntPtr.Zero) GdipDeletePath(path);
        if (matrix != IntPtr.Zero) GdipDeleteMatrix(matrix);
        if (softInk != IntPtr.Zero) GdipDeleteBrush(softInk);
        if (hoverInk != IntPtr.Zero) GdipDeleteBrush(hoverInk);
        if (lockedInk != IntPtr.Zero) GdipDeleteBrush(lockedInk);
        if (hoverBackground != IntPtr.Zero) GdipDeleteBrush(hoverBackground);
        GdiplusShutdown(token);
        token = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StartupInput
    {
        public uint Version;
        public IntPtr DebugCallback;
        public int SuppressBackgroundThread, SuppressExternalCodecs;
    }

    const string Library = "gdiplus.dll";
    [DllImport(Library, ExactSpelling = true)] static extern int GdiplusStartup(out nuint token, ref StartupInput input, IntPtr output);
    [DllImport(Library, ExactSpelling = true)] static extern void GdiplusShutdown(nuint token);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipCreateFromHDC(IntPtr dc, out IntPtr graphics);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipDeleteGraphics(IntPtr graphics);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipSetSmoothingMode(IntPtr graphics, int mode);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipSetPixelOffsetMode(IntPtr graphics, int mode);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipSetCompositingQuality(IntPtr graphics, int quality);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipSetPageUnit(IntPtr graphics, int unit);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipFlush(IntPtr graphics, int intention);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipCreateSolidFill(uint color, out IntPtr brush);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipDeleteBrush(IntPtr brush);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipCreateMatrix(out IntPtr matrix);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipSetMatrixElements(IntPtr matrix, float m11, float m12, float m21, float m22, float dx, float dy);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipSetWorldTransform(IntPtr graphics, IntPtr matrix);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipDeleteMatrix(IntPtr matrix);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipCreatePath(int fillMode, out IntPtr path);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipStartPathFigure(IntPtr path);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipClosePathFigure(IntPtr path);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipAddPathLine(IntPtr path, float x1, float y1, float x2, float y2);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipAddPathBezier(IntPtr path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipFillPath(IntPtr graphics, IntPtr brush, IntPtr path);
    [DllImport(Library, ExactSpelling = true)] static extern int GdipDeletePath(IntPtr path);
}
