using System;
using System.Collections.Generic;
using ProxyTrafficFloat;

namespace ProxyTrafficFloat.Native;

/// <summary>Double-buffered native painting. GDI resources live for the canvas lifetime, never per frame.</summary>
public sealed class Canvas : IDisposable
{
    IntPtr dc, bitmap, oldBitmap;
    IntPtr background, surface;
    int themeVersion;
    IntPtr border, downPen, upPen, linePen;
    int width, height;
    double scale;
    bool englishFonts;
    readonly Dictionary<(int Size, int Weight), IntPtr> fonts = new();
    readonly Win.Point[] path = new Win.Point[3];
    readonly HeaderGlyphs glyphs = new();

    // Win32 COLORREF stores colors in BGR order; this matches the settings palette.
    uint Ink => AppTheme.Bgr("Ink");
    uint Muted => AppTheme.Bgr("Muted");
    uint Down => AppTheme.Bgr("Accent");
    uint Up => AppTheme.Bgr("Upload");
    uint Line => AppTheme.Bgr("Line");

    public Canvas()
    {
        var screen = Win.GetDC(IntPtr.Zero);
        dc = Win.CreateCompatibleDC(screen);
        Win.ReleaseDC(IntPtr.Zero, screen);
        AppTheme.Apply("system");
        background = Win.CreateSolidBrush(AppTheme.Bgr("Canvas"));
        surface = Win.CreateSolidBrush(AppTheme.Bgr("Surface"));
        themeVersion = AppTheme.Version;
        Win.SetBkMode(dc, 1);
    }

    public void Resize(int newWidth, int newHeight, double newScale)
    {
        if (Math.Abs(scale - newScale) > 0.001)
        {
            // Unselect before deleting: selected GDI objects cannot be released.
            Win.SelectObject(dc, Win.GetStockObject(17));
            ClearFonts();
            scale = newScale;
            RebuildPens();
        }
        if (width == newWidth && height == newHeight) return;
        width = newWidth;
        height = newHeight;
        glyphs.Unbind();
        if (bitmap != IntPtr.Zero)
        {
            Win.SelectObject(dc, oldBitmap);
            Win.DeleteObject(bitmap);
        }
        var screen = Win.GetDC(IntPtr.Zero);
        bitmap = Win.CreateCompatibleBitmap(screen, width, height);
        Win.ReleaseDC(IntPtr.Zero, screen);
        var previous = Win.SelectObject(dc, bitmap);
        if (oldBitmap == IntPtr.Zero) oldBitmap = previous;
        glyphs.Bind(dc);
    }

    void RebuildPens()
    {
        // Pen widths are in physical pixels. Rebuild only when display scale changes.
        ReleasePens();
        int outlineWidth = Math.Max(1, P(1.0));
        int strokeWidth = Math.Max(1, P(1.2));
        border = Win.CreatePen(0, outlineWidth, Line);
        downPen = Win.CreatePen(0, strokeWidth, Down);
        upPen = Win.CreatePen(0, strokeWidth, Up);
        linePen = Win.CreatePen(0, strokeWidth, Line);
    }

    void ReleasePens()
    {
        // A selected GDI pen cannot be deleted. Keep the stock pen in the DC while replacing caches.
        Win.SelectObject(dc, Win.GetStockObject(19));
        if (border != IntPtr.Zero) Win.DeleteObject(border);
        if (downPen != IntPtr.Zero) Win.DeleteObject(downPen);
        if (upPen != IntPtr.Zero) Win.DeleteObject(upPen);
        if (linePen != IntPtr.Zero) Win.DeleteObject(linePen);
    }
    int P(double logical) => (int)Math.Round(logical * scale);
    void ClearFonts()
    {
        Win.SelectObject(dc, Win.GetStockObject(17));
        foreach (var font in fonts.Values) Win.DeleteObject(font);
        fonts.Clear();
    }

    void Text(string text, int x, int y, int w, int h, int size, uint color, uint alignment = 0, int weight = 400)
    {
        var key = (size, weight);
        if (!fonts.TryGetValue(key, out var font))
        {
            font = Win.CreateFont(-P(size), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, englishFonts ? "Segoe UI" : "Microsoft YaHei");
            fonts[key] = font;
        }
        var previous = Win.SelectObject(dc, font);
        Win.SetTextColor(dc, color);
        var rect = new Win.Rect { Left = P(x), Top = P(y), Right = P(x + w), Bottom = P(y + h) };
        Win.DrawText(dc, text, text.Length, ref rect, 0x20 | 0x8000 | 0x800 | alignment);
        Win.SelectObject(dc, previous);
    }

    void RoundedPanel(int x, int y, int w, int h, IntPtr brush, int radius = 9)
    {
        var oldBrush = Win.SelectObject(dc, brush);
        var oldPen = Win.SelectObject(dc, border);
        Win.RoundRect(dc, P(x), P(y), P(x + w), P(y + h), P(radius), P(radius));
        Win.SelectObject(dc, oldBrush);
        Win.SelectObject(dc, oldPen);
    }

    void Stroke(uint color, params ReadOnlySpan<(double X, double Y)> points)
    {
        // Inline points stay on the stack; DPI-aware color pens are cached for the canvas lifetime.
        IntPtr pen = color == Down ? downPen : color == Up ? upPen : linePen;
        var previous = Win.SelectObject(dc, pen);
        for (int i = 0; i < points.Length; i++)
            path[i] = new Win.Point { X = P(points[i].X), Y = P(points[i].Y) };
        Win.Polyline(dc, path, points.Length);
        Win.SelectObject(dc, previous);
    }

    void Arrow(int x, int y, bool down, uint color)
    {
        Stroke(color, (x + 3, y), (x + 3, y + 9));
        Stroke(color, down ? (x, y + 6) : (x, y + 3),
            down ? (x + 3, y + 9) : (x + 3, y),
            down ? (x + 6, y + 6) : (x + 6, y + 3));
    }

    public void Render(NativeFrame frame, Settings config, bool mini, int hoverButton = -1)
    {
        AppTheme.Apply(config.ColorMode);
        if (themeVersion != AppTheme.Version)
        {
            Win.SelectObject(dc, Win.GetStockObject(5));
            Win.DeleteObject(background); Win.DeleteObject(surface);
            background=Win.CreateSolidBrush(AppTheme.Bgr("Canvas"));
            surface=Win.CreateSolidBrush(AppTheme.Bgr("Surface"));
            RebuildPens(); themeVersion=AppTheme.Version;
        }
        if (englishFonts != Strings.IsEnglish)
        {
            ClearFonts();
            englishFonts = Strings.IsEnglish;
        }
        var rect = new Win.Rect { Right = width, Bottom = height };
        Win.FillRect(dc, ref rect, background);
        int logicalWidth = (int)Math.Round(width / scale);
        RoundedPanel(0, 0, logicalWidth, (int)Math.Round(height / scale), background, 18);
        var view = frame.View;
        if (mini)
        {
            Arrow(10, 13, true, Down);
            Arrow(10, 35, false, Up);
            Text(view.DownloadTop?.Target ?? Strings.T(frame.Connected ? "空闲" : "未连接"), 24, 8, logicalWidth - 141, 20, 11, Ink);
            Text(view.UploadTop?.Target ?? Strings.T(frame.Connected ? "空闲" : "未连接"), 24, 30, logicalWidth - 141, 20, 11, Ink);
            Text(Display.Speed(view.DownloadTop?.DownloadSpeed ?? 0), logicalWidth - 113, 8, 81, 20, 12, Down, 2);
            Text(Display.Speed(view.UploadTop?.UploadSpeed ?? 0), logicalWidth - 113, 30, 81, 20, 12, Up, 2);
            glyphs.Draw(scale, logicalWidth, true, config.Locked, hoverButton);
            return;
        }

        Arrow(12, 9, true, Down);
        Arrow(22, 9, false, Up);
        Text("TRAFFIC LENS", 35, 7, logicalWidth - 133, 18, 9, Muted, weight: 600);
        glyphs.Draw(scale, logicalWidth, false, config.Locked, hoverButton);
        int y = 28;
        if (config.ShowSpeed)
        {
            int half = (logicalWidth - 28) / 2;
            Arrow(13, y + 6, true, Down);
            Arrow(17 + half, y + 6, false, Up);
            Text(Display.Speed(view.DownloadSpeed), 25, y, half - 13, 20, 12, Ink, weight: 600);
            Text(Display.Speed(view.UploadSpeed), 29 + half, y, half - 13, 20, 12, Ink, weight: 600);
            y += 22;
        }
        if (config.ShowDownload || config.ShowUpload)
        {
            bool both = config.ShowDownload && config.ShowUpload;
            int columnWidth = both ? (logicalWidth - 36) / 2 : logicalWidth - 24;
            if (config.ShowDownload)
                Target(view.DownloadTop, true, 12, y, columnWidth, frame.DownloadTargetTotal, config.ShowTargetTotal, frame.Connected);
            if (config.ShowUpload)
                Target(view.UploadTop, false, config.ShowDownload ? 24 + columnWidth : 12, y, columnWidth,
                    frame.UploadTargetTotal, config.ShowTargetTotal, frame.Connected);
            y += config.ShowTargetTotal ? 91 : 76;
        }
        if (config.ShowTotal)
        {
            Text(Strings.Format("今日 ↓ {0}   ↑ {1}", Display.Bytes(frame.TodayDownload), Display.Bytes(frame.TodayUpload)),
                12, y, logicalWidth - 24, 18, 10, Muted);
            y += 19;
        }
        Stroke(Line, (12, y + 2), (logicalWidth - 12, y + 2));
        Text(frame.Connected ? "●" : "○", 12, y + 4, 10, 16, 9, frame.Connected ? Down : Muted);
        Text(frame.Connected ? Strings.Format("代理 {0} · 未知 {1}", view.ProxyCount, view.UnknownCount) : Strings.T("连接中断 · 自动重连"),
            26, y + 4, logicalWidth - 38, 16, 9, Muted);
    }

    void Target(RankedTarget? target, bool down, int x, int y, int w, long total, bool daily, bool connected)
    {
        uint color = down ? Down : Up;
        RoundedPanel(x, y, w, daily ? 88 : 73, surface);
        int contentX = x + 9, contentWidth = w - 18;
        Text(Strings.T(down ? "下载最快" : "上传最快"), contentX, y + 5, contentWidth, 14, 9, Muted);
        Text(target?.Target ?? Strings.T(connected ? "暂无流量" : "等待连接"), contentX, y + 20, contentWidth, 18, 11, Ink);
        Text(Display.Speed(target == null ? 0 : down ? target.DownloadSpeed : target.UploadSpeed),
            contentX, y + 37, contentWidth, 21, 16, color, weight: 600);
        string metadata = target == null ? "—" : target.Process == "未知" ? Strings.T(target.Node) : Strings.T(target.Process) + " · " + Strings.T(target.Node);
        Text(metadata, contentX, y + 58, contentWidth, 14, 9, Muted);
        if (daily) Text(Strings.Format("今日 {0}", Display.Bytes(total)), contentX, y + 73, contentWidth, 14, 9, Muted);
    }

    public void PaintPart(IntPtr target, int x, int y, int w, int h)
    {
        if (bitmap != IntPtr.Zero) Win.BitBlt(target, 0, 0, w, h, dc, x, y, 0x00CC0020);
    }

    public void Paint(IntPtr target)
    {
        if (bitmap != IntPtr.Zero) Win.BitBlt(target, 0, 0, width, height, dc, 0, 0, 0x00CC0020);
    }

    public void SaveBitmap(string file)
    {
        var bytes = new byte[width * height * 4];
        var info = new Win.BitmapInfo { Size = 40, Width = width, Height = height, Planes = 1, Bits = 32, ImageSize = (uint)bytes.Length };
        Win.SelectObject(dc, oldBitmap);
        try
        {
            if (Win.GetDIBits(dc, bitmap, 0, (uint)height, bytes, ref info, 0) == 0)
                throw new InvalidOperationException("无法导出画布");
        }
        finally { Win.SelectObject(dc, bitmap); }
        using var writer = new System.IO.BinaryWriter(System.IO.File.Create(file));
        writer.Write((ushort)0x4D42);
        writer.Write(54 + bytes.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(bytes.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(bytes);
    }

    public void Dispose()
    {
        if (dc == IntPtr.Zero) return;
        glyphs.Dispose();
        Win.SelectObject(dc, Win.GetStockObject(17));
        foreach (var font in fonts.Values) Win.DeleteObject(font);
        if (bitmap != IntPtr.Zero)
        {
            Win.SelectObject(dc, oldBitmap);
            Win.DeleteObject(bitmap);
        }
        Win.DeleteObject(background);
        Win.DeleteObject(surface);
        ReleasePens();
        Win.DeleteDC(dc);
        dc = IntPtr.Zero;
    }
}
