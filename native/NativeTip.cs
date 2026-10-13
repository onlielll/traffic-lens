using System;
using System.Runtime.InteropServices;
using ProxyTrafficFloat;

namespace ProxyTrafficFloat.Native;

public sealed class NativeTip : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Init
    {
        public uint Size, Classes;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Tool
    {
        public uint Size, Flags; public IntPtr Hwnd; public UIntPtr Id; public Win.Rect Rect; public IntPtr Instance, Text, Param, Reserved;
    }
    [DllImport("comctl32.dll")] static extern bool InitCommonControlsEx(ref Init init);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string classes);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] static extern IntPtr SendTool(IntPtr hwnd, uint message, IntPtr w, ref Tool tool);
    IntPtr window, text; Tool tool; string previous = "";
    int themeVersion;
    public NativeTip(IntPtr owner)
    {
        var init = new Init { Size = 8, Classes = 0xff };
        InitCommonControlsEx(ref init);
        window = Win.CreateWindowEx(0x08000008, "tooltips_class32", "", 0x80000003, 0, 0, 0, 0, owner, IntPtr.Zero, Win.GetModuleHandle(null), IntPtr.Zero);
        if (window == IntPtr.Zero)
            return;
        SetWindowTheme(window, "", "");
        ApplyTheme();
        Win.SendMessage(window, 0x418, IntPtr.Zero, new IntPtr(420));
        Win.SendMessage(window, 0x403, new IntPtr(3), new IntPtr(500));
        Win.SendMessage(window, 0x403, new IntPtr(2), new IntPtr(12000));
        text = Marshal.StringToHGlobalUni("代理流量");
        tool = new Tool { Size = (uint)Marshal.SizeOf<Tool>(), Flags = 0x11, Hwnd = owner, Id = (UIntPtr)owner, Text = text };
        SendTool(window, 0x432, IntPtr.Zero, ref tool);
    }
    public void Enable(bool enabled)
    {
        if (window != IntPtr.Zero)
            Win.SendMessage(window, 0x401, enabled ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero);
    }
    void ApplyTheme()
    {
        if(window==IntPtr.Zero||themeVersion==AppTheme.Version) return;
        Win.SendMessage(window,0x413,new IntPtr(AppTheme.Bgr("Surface")),IntPtr.Zero);
        Win.SendMessage(window,0x414,new IntPtr(AppTheme.Bgr("Ink")),IntPtr.Zero);
        themeVersion=AppTheme.Version;
    }
    public void Update(string value)
    {
        ApplyTheme();
        if (window == IntPtr.Zero || value == previous)
            return;
        previous = value;
        var fresh = Marshal.StringToHGlobalUni(value);
        var old = text;
        text = fresh;
        tool.Text = fresh;
        SendTool(window, 0x439, IntPtr.Zero, ref tool);
        Marshal.FreeHGlobal(old);
    }
    public void Dispose()
    {
        if (window != IntPtr.Zero)
        {
            Win.DestroyWindow(window);
            window = IntPtr.Zero;
        }
        if (text != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(text);
            text = IntPtr.Zero;
        }
    }
}
