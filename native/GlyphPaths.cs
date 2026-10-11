using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ProxyTrafficFloat.Native;

/// <summary>Heroicons MIT vector data is decoded once; GDI+ owns its copied paths thereafter.</summary>
internal static class GlyphPaths
{
    [StructLayout(LayoutKind.Sequential)]
    struct PointF { public float X, Y; }

    public static void Load(IntPtr[] paths)
    {
        using var reader = new BinaryReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("TrafficLens.ToolbarPaths")!);
        int glyphs = reader.ReadInt32();
        for (int glyph = 0; glyph < glyphs; glyph++)
        {
            int fill = reader.ReadInt32(), count = reader.ReadInt32();
            var points = new PointF[count];
            var types = new byte[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = new PointF { X = reader.ReadSingle(), Y = reader.ReadSingle() };
                types[i] = reader.ReadByte();
            }
            int result = GdipCreatePath2(points, types, count, fill, out paths[glyph]);
            if (result != 0) throw new InvalidOperationException("Toolbar vector initialization failed: " + result);
        }
    }

    [DllImport("gdiplus.dll", ExactSpelling = true)]
    static extern int GdipCreatePath2(PointF[] points, byte[] types, int count, int fillMode, out IntPtr path);
}
