using System;
using System.Collections.Generic;

namespace ProxyTrafficFloat;

public readonly record struct CurvePoint(double X, double Y);

/// <summary>Monotone Hermite interpolation: smooth joins without inventing peaks or negative traffic.</summary>
public static class ChartMath
{
    public static List<CurvePoint> Smooth(IReadOnlyList<CurvePoint> points)
    {
        var result = new List<CurvePoint>(Math.Max(0, points.Count * 8));
        int begin = 0;
        while (begin < points.Count)
        {
            if (!double.IsFinite(points[begin].X) || !double.IsFinite(points[begin].Y))
            {
                result.Add(points[begin++]);
                continue;
            }
            int end = begin + 1;
            while (end < points.Count && double.IsFinite(points[end].X) && double.IsFinite(points[end].Y))
                end++;
            int count = end - begin;
            if (count == 1)
            {
                result.Add(points[begin]);
                begin = end;
                continue;
            }
            var slopes = new double[count - 1];
            var widths = new double[count - 1];
            for (int i = 0; i < count - 1; i++)
            {
                widths[i] = points[begin + i + 1].X - points[begin + i].X;
                slopes[i] = widths[i] == 0 ? 0 : (points[begin + i + 1].Y - points[begin + i].Y) / widths[i];
            }
            var tangents = new double[count];
            tangents[0] = slopes[0];
            tangents[count - 1] = slopes[count - 2];
            for (int i = 1; i < count - 1; i++)
            {
                if (slopes[i - 1] * slopes[i] <= 0)
                    continue;
                double a = 2 * widths[i] + widths[i - 1], b = widths[i] + 2 * widths[i - 1];
                tangents[i] = (a + b) / (a / slopes[i - 1] + b / slopes[i]);
            }
            for (int i = 0; i < count - 1; i++)
            {
                var left = points[begin + i];
                var right = points[begin + i + 1];
                for (int j = 0; j < 8; j++)
                {
                    double t = j / 8.0, t2 = t * t, t3 = t2 * t;
                    double value = (2 * t3 - 3 * t2 + 1) * left.Y + (t3 - 2 * t2 + t) * widths[i] * tangents[i] + (-2 * t3 + 3 * t2) * right.Y + (t3 - t2) * widths[i] * tangents[i + 1];
                    result.Add(new(left.X + widths[i] * t, Math.Clamp(value, Math.Min(left.Y, right.Y), Math.Max(left.Y, right.Y))));
                }
            }
            result.Add(points[end - 1]);
            begin = end;
        }
        return result;
    }
    // Aggregation affects drawing only. Interval totals always come from the original stored bytes.
    public static int Step(double span, bool perMinute)
    {
        int needed = Math.Max(perMinute ? 60 : 1, (int)Math.Ceiling(span / 1200));
        foreach (int value in new[] { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 })
        if (value >= needed)
            return value;
        return 3600;
    }
}
