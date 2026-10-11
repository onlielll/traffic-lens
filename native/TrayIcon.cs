using System;
using System.IO;
using System.Reflection;

namespace ProxyTrafficFloat.Native;

public static class TrayIcon
{
    public static IntPtr Create()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ProxyTrafficFloat.traffic.ico") ?? throw new IOException("缺少流量图标");
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16();
        reader.ReadUInt16();
        int count = reader.ReadUInt16();
        uint length = 0, offset = 0;
        for (int i = 0; i < count; i++)
        {
            int size = reader.ReadByte();
            reader.ReadByte();
            reader.ReadUInt16();
            reader.ReadUInt16();
            reader.ReadUInt16();
            uint n = reader.ReadUInt32(), at = reader.ReadUInt32();
            if (size == 32)
            {
                length = n;
                offset = at;
            }
        }
        if (length == 0)
            throw new IOException("流量图标格式无效");
        stream.Position = offset;
        byte[] data = reader.ReadBytes((int)length);
        var icon = Win.CreateIconFromResourceEx(data, (uint)data.Length, true, 0x30000, 32, 32, 0);
        if (icon == IntPtr.Zero)
            throw new IOException("无法创建托盘图标");
        return icon;
    }
}
