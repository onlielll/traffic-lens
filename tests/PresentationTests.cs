using System;
using ProxyTrafficFloat;
public static class PresentationTests
{
    public static void Run()
    {
        int assertions = 0;
        void Assert(bool ok, string label) { assertions++; if (!ok) throw new Exception(label); }
        var mode = new CompactModeController();
        bool Update(double now, double down, double up = 0, bool connected = true, bool auto = true) => mode.Update(true, auto, connected, down, up, 1024, 512, 2, 10, now);
        Assert(!Update(0, 1024), "first threshold does not flash"); Assert(!Update(1, 1024), "wait sustained two seconds"); Assert(Update(2, 1024), "two seconds expands");
        Assert(Update(3, 700), "hysteresis intermediate retains detail"); Assert(Update(4, 0), "quiet starts countdown"); Assert(Update(13, 0), "quiet waits ten seconds"); Assert(!Update(14, 0), "quiet collapses");
        Assert(!Update(15, 0, 512), "upload starts trigger"); Assert(Update(17, 0, 512), "upload expands"); Assert(!Update(18, 9999, 9999, false), "disconnection collapses");
        mode.ManualExpand(20); Assert(Update(21, 0, 0, true, false), "manual expand overrides disabled auto"); Assert(!Update(35, 0, 0, true, false), "manual lease expires");
        mode.ManualExpand(36); Assert(Update(37, 0, 0, false), "manual inspection allowed when disconnected");
        mode.Collapse(40); Assert(!Update(41, 9999), "manual collapse suppression"); Assert(!Update(55, 9999), "threshold resumes baseline"); Assert(Update(57, 9999), "automatic resumes after suppression");
        Assert(!mode.Update(false, true, true, 9999, 9999, 1024, 512, 2, 10, 58), "normal mode resets");
        foreach (var (width, height) in new[] { (800, 560), (1366, 728), (1920, 1040), (3840, 2080) })
            foreach (var dpi in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
                foreach (var corner in new[] { 0, 1, 2, 3 })
                {
                    double ww = Math.Min(220 * dpi, width - 16), hh = 58 * dpi;
                    var p = ScreenGeometry.Corner(-width, 0, width, height, ww, hh, corner, 8 * dpi);
                    Assert(p.X >= -width && p.Y >= 0 && p.X + ww <= 0 && p.Y + hh <= height, "mini within negative monitor workarea");
                    var detail = ScreenGeometry.Corner(0, 0, width, height, Math.Min(304 * dpi, width - 16), Math.Min(180 * dpi, height - 16), corner, 8 * dpi);
                    Assert(detail.X >= 0 && detail.Y >= 0 && detail.X + Math.Min(304 * dpi, width - 16) <= width && detail.Y + Math.Min(180 * dpi, height - 16) <= height, "detail within resolution and scale");
                }
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
        {
            var near = ScreenGeometry.Snap(-1915, 7, -1920, 0, 1920, 1040, 304 * dpi, 162 * dpi, 16 * dpi); Assert(near.X == -1920 && near.Y == 0, "snap negative left/top edge");
            var right = ScreenGeometry.Snap(1920 - 304 * dpi - 8, 1040 - 162 * dpi - 8, 0, 0, 1920, 1040, 304 * dpi, 162 * dpi, 16 * dpi); Assert(right.X == 1920 - 304 * dpi && right.Y == 1040 - 162 * dpi, "snap right/bottom");
            var free = ScreenGeometry.Snap(300, 250, 0, 0, 1920, 1040, 220 * dpi, 58 * dpi, 16 * dpi); Assert(free.X == 300 && free.Y == 250, "free mini position unchanged");
        }
        Console.WriteLine($"PASS: {assertions} presentation assertions (threshold, hysteresis, manual override, 4 resolutions × 5 DPI scales × 4 corners)");
    }
}
