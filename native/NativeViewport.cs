using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using ProxyTrafficFloat;

namespace ProxyTrafficFloat.Native;

public static partial class Win
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder name, int length);
}

public sealed partial class NativeMonitor
{
    bool fullscreenHidden;
    double fullscreenCheckedAt = -1;
    readonly StringBuilder foregroundClass = new(64);
    int resizeCorner;
    double resizeScale, resizeDrawScale;
    Screen? resizeScreen;

    bool FullscreenWindow(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero || foreground == hwnd || foreground == unlockWindow || !Win.IsWindowVisible(foreground) || Win.IsIconic(foreground)) return false;
        Win.GetClassName(foreground, foregroundClass, foregroundClass.Capacity);
        string name = foregroundClass.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var info = new Win.MonitorInfo { Size = Marshal.SizeOf<Win.MonitorInfo>() };
        return Win.MonitorFromWindow(foreground, 2) == Win.MonitorFromWindow(hwnd, 2) && Win.GetWindowRect(foreground, out var bounds) && Win.GetMonitorInfo(Win.MonitorFromWindow(foreground, 2), ref info) &&
            ScreenGeometry.CoversScreen(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
    }

    void RefreshFullscreen()
    {
        if (watch.Elapsed.TotalSeconds - fullscreenCheckedAt < 1) return;
        fullscreenCheckedAt = watch.Elapsed.TotalSeconds;
        bool hidden = config.HideInFullscreen && FullscreenWindow(Win.GetForegroundWindow());
        if (hidden == fullscreenHidden) return;
        fullscreenHidden = hidden;
        ApplyVisibility();
        tip?.Enable(!hidden && !config.Locked);
    }

    int ResizeHit(int x, int y)
    {
        if (config.Locked || config.ClickThrough) return 0;
        int margin = Math.Max(6, (int)Math.Round(9 * drawScale));
        bool left = x >= 0 && x < margin, right = x <= pixelWidth && x > pixelWidth - margin;
        bool top = y >= 0 && y < margin, bottom = y <= pixelHeight && y > pixelHeight - margin;
        return top ? left ? 13 : right ? 14 : 0 : bottom ? left ? 16 : right ? 17 : 0 : 0;
    }

    void BeginPointerResize(int corner, Win.Point point)
    {
        resizeCorner = corner;
        resizeScale = config.UiScale;
        resizeDrawScale = drawScale;
        resizeScreen = CurrentScreen(false);
        dragOrigin = point;
        Win.GetWindowRect(hwnd, out dragBounds);
        dragArmed = dragging = true;
        dragWasMini = mini;
        Win.SetCapture(hwnd);
        tip?.Enable(false);
    }

    void MovePointerResize(Win.Point point)
    {
        var screen = resizeScreen!;
        int originalWidth = dragBounds.Right - dragBounds.Left, originalHeight = dragBounds.Bottom - dragBounds.Top;
        int dx = point.X - dragOrigin.X, dy = point.Y - dragOrigin.Y;
        double x = 1 + (resizeCorner is 13 or 16 ? -dx : dx) / (double)originalWidth;
        double y = 1 + (resizeCorner is 13 or 14 ? -dy : dy) / (double)originalHeight;
        // Keep the compact layout proportional instead of stretching text or creating empty space.
        double ratio = Math.Abs(x - 1) >= Math.Abs(y - 1) ? x : y;
        config.UiScale = Math.Clamp(resizeScale * ratio, 0.8, 1.25);
        ratio = config.UiScale / resizeScale;
        double room = Math.Min((screen.Work.Right - screen.Work.Left - 16 * screen.Scale) / originalWidth,
            (screen.Work.Bottom - screen.Work.Top - 16 * screen.Scale) / originalHeight);
        ratio = Math.Min(ratio, room);
        int width = Math.Max(1, (int)Math.Round(originalWidth * ratio)), height = Math.Max(1, (int)Math.Round(originalHeight * ratio));
        if (width == pixelWidth && height == pixelHeight) return;
        drawScale = resizeDrawScale * ratio;
        int left = resizeCorner is 13 or 16 ? dragBounds.Right - width : dragBounds.Left;
        int top = resizeCorner is 13 or 14 ? dragBounds.Bottom - height : dragBounds.Top;
        var position = ScreenGeometry.Clamp(left, top, screen.Work.Left, screen.Work.Top, screen.Work.Right - screen.Work.Left, screen.Work.Bottom - screen.Work.Top, width, height);
        Win.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y), width, height, 0x0014);
        ResizeCanvas(width, height);
        Win.InvalidateRect(hwnd, IntPtr.Zero, false);
    }

    void ResizeCanvas(int width, int height)
    {
        if (pixelWidth != width || pixelHeight != height)
        {
            pixelWidth = width; pixelHeight = height;
            var region = Win.CreateRoundRectRgn(0, 0, width + 1, height + 1, (int)(18 * drawScale), (int)(18 * drawScale));
            if (!Win.SetWindowRgn(hwnd, region, true)) Win.DeleteObject(region);
        }
        canvas.Resize(width, height, drawScale);
    }
}