using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace ProxyTrafficFloat;

/// <summary>Shared colors for native painting and WPF. System changes are delivered by WM_SETTINGCHANGE.</summary>
public static class AppTheme
{
    public static string Mode { get; private set; } = "";
    public static bool IsDark
    {
        get; private set;
    }
    public static int Version
    {
        get; private set;
    }
    public static IReadOnlyDictionary<string, uint> Colors => IsDark ? Dark : Light;
    public static string Normalize(string? mode) => mode is "light" or "dark" ? mode : "system";
    public static bool Apply(string? mode, bool systemChanged = false)
    {
        string choice = Normalize(mode);
        if (choice == Mode && !systemChanged)
            return false;
        bool dark = choice == "dark" || choice == "system" && SystemDark();
        bool changed = Version == 0 || dark != IsDark;
        Mode = choice;
        IsDark = dark;
        if (changed)
            Version++;
        return changed;
    }
    static bool SystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
    public static uint Color(string key) => Colors[key];
    public static uint Bgr(string key)
    {
        uint c = Color(key);
        return (c & 255) << 16 | (c & 0xff00) | (c >> 16 & 255);
    }
    static readonly Dictionary<string, uint> Dark = new()
    {
        ["Canvas"] = 0x15171D,
        ["Surface"] = 0x20242D,
        ["Line"] = 0x333943,
        ["Ink"] = 0xEEF3FB,
        ["Muted"] = 0xB9C0CE,
        ["Accent"] = 0x62D8BD,
        ["Upload"] = 0xB1A3F9,
        ["Control"] = 0x2C3240,
        ["ControlHover"] = 0x394253,
        ["ControlBorder"] = 0x415169,
        ["Input"] = 0x101722,
        ["Selection"] = 0x304353,
        ["ToggleOff"] = 0x46536B,
        ["Knob"] = 0xF4F8FE,
        ["AccentInk"] = 0x10241F,
        ["Header"] = 0x202B3C,
        ["Danger"] = 0xF4ACB4,
        ["DangerBackground"] = 0x352733
    };
    static readonly Dictionary<string, uint> Light = new()
    {
        ["Canvas"] = 0xF5F5F7,
        ["Surface"] = 0xFFFFFF,
        ["Line"] = 0xDCE3ED,
        ["Ink"] = 0x17212F,
        ["Muted"] = 0x526176,
        ["Accent"] = 0x007F69,
        ["Upload"] = 0x6D4BD1,
        ["Control"] = 0xEDF1F7,
        ["ControlHover"] = 0xE2E9F3,
        ["ControlBorder"] = 0xCAD4E1,
        ["Input"] = 0xFFFFFF,
        ["Selection"] = 0xDEEAEF,
        ["ToggleOff"] = 0xB7C3D2,
        ["Knob"] = 0xFFFFFF,
        ["AccentInk"] = 0xFFFFFF,
        ["Header"] = 0xF0F3F8,
        ["Danger"] = 0xAF3143,
        ["DangerBackground"] = 0xFCECEF
    };
}
