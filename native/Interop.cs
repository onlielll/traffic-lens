using System;
using System.Runtime.InteropServices;

namespace ProxyTrafficFloat.Native;

public static class Win
{
    public delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WindowClass
    {
        public uint Size, Style; public WindowProc Proc; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; [MarshalAs(UnmanagedType.LPWStr)] public string? Menu; [MarshalAs(UnmanagedType.LPWStr)] public string Name; public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseTracking
    {
        public uint Size, Flags; public IntPtr Window; public uint HoverTime;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public IntPtr Hwnd; public uint Id; public IntPtr W, L; public uint Time; public Point Point; public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Paint
    {
        public IntPtr DC; public int Erase; public Rect Rect; public int Restore, Incremental; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct IconData
    {
        public uint Size; public IntPtr Hwnd; public uint Id, Flags, Callback; public IntPtr Icon; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip; public uint State, StateMask; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info; public uint Version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title; public uint InfoFlags; public Guid Guid; public IntPtr Balloon;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int X, Y; public uint Used, Important;
    }
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint first, uint count, byte[] pixels, ref BitmapInfo info, uint usage);
    public delegate bool MonitorProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr arg);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] public static extern int GetMessage(out Message msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref Message msg);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr hwnd, string text);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, out Paint paint);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref Paint paint);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetStyle(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetStyle(IntPtr hwnd, int index, int flags);
    [DllImport("user32.dll")] public static extern bool SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint interval, IntPtr callback);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetCapture();
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref MouseTracking tracking);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] public static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] public static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] public static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr cursor);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(uint action, ref IconData data);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr arg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint RegisterWindowMessage(string message);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] public static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] public static extern bool Polyline(IntPtr dc, Point[] points, int count);
    [DllImport("gdi32.dll")] public static extern uint SetDCPenColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll")] public static extern bool RoundRect(IntPtr dc, int left, int top, int right, int bottom, int ellipseW, int ellipseH);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] public static extern int FillRect(IntPtr dc, ref Rect rect, IntPtr brush);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateFont(int height, int width, int esc, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint output, uint clip, uint quality, uint pitch, string face);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawText(IntPtr dc, string text, int count, ref Rect rect, uint flags);
    [DllImport("gdi32.dll")] public static extern IntPtr GetStockObject(int id);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(IntPtr hwnd, string text, string caption, uint flags);
}
