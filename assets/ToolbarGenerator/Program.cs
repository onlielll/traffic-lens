using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

// Build-time only: preserve the licensed SVG originals; the resident loads compact vector data.
string directory = Path.GetFullPath(args.Length > 0 ? args[0] : "assets/heroicons");
string[] icons = ["cog-6-tooth", "arrows-pointing-in", "lock-closed", "lock-open", "minus"];
using var writer = new BinaryWriter(File.Create(Path.Combine(directory, "glyphs.bin")));
writer.Write(icons.Length);
foreach (string icon in icons)
{
    var document = XDocument.Load(Path.Combine(directory, icon + ".svg"));
    var points = new List<Point>();
    var types = new List<byte>();
    foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "path"))
    {
        var geometry = Geometry.Parse(element.Attribute("d")!.Value).GetFlattenedPathGeometry(0.015, ToleranceType.Absolute);
        foreach (var figure in geometry.Figures)
        {
            points.Add(figure.StartPoint);
            types.Add(0);
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment poly)
                    foreach (var point in poly.Points) { points.Add(point); types.Add(1); }
                else if (segment is LineSegment line) { points.Add(line.Point); types.Add(1); }
            }
            if (figure.IsClosed) types[^1] |= 0x80;
        }
    }
    writer.Write(0); // Heroicons use even-odd fill, including the lock shackle and gear cutouts.
    writer.Write(points.Count);
    for (int i = 0; i < points.Count; i++)
    {
        writer.Write((float)points[i].X);
        writer.Write((float)points[i].Y);
        writer.Write(types[i]);
    }
    Console.WriteLine($"{icon}: {points.Count} points");
}
