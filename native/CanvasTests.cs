using System;

namespace ProxyTrafficFloat.Native;

public sealed partial class Canvas
{
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

}
