using System.Collections.Generic;
using System.Linq;
using OxyPlot;

namespace ProxyTrafficFloat;

public sealed class ShapePreservingSpline : IInterpolationAlgorithm
{
    public List<DataPoint> CreateSpline(List<DataPoint> points, bool isClosed, double tolerance) =>
        ChartMath.Smooth(points.Select(p => new CurvePoint(p.X, p.Y)).ToArray()).Select(p => new DataPoint(p.X, p.Y)).ToList();
    public List<ScreenPoint> CreateSpline(IList<ScreenPoint> points, bool isClosed, double tolerance) =>
        ChartMath.Smooth(points.Select(p => new CurvePoint(p.X, p.Y)).ToArray()).Select(p => new ScreenPoint(p.X, p.Y)).ToList();
}
