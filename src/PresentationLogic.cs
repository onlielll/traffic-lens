using System;

namespace ProxyTrafficFloat;

public sealed class CompactModeController
{
    double? aboveSince, belowSince;
    bool expanded;
    double manualUntil, suppressUntil;

    public bool IsExpanded => expanded;

    public void Reset()
    {
        aboveSince = belowSince = null;
        expanded = false;
        manualUntil = suppressUntil = 0;
    }

    public void ManualExpand(double now)
    {
        expanded = true;
        manualUntil = now + 15;
        aboveSince = belowSince = null;
    }

    public void Collapse(double now = 0)
    {
        Reset();
        suppressUntil = now + 15;
    }

    public bool Update(bool enabled, bool autoExpand, bool connected, double down, double up,
        double downThreshold, double upThreshold, double triggerSeconds, double quietSeconds, double now)
    {
        if (!enabled)
        {
            Reset();
            return false;
        }
        if (now < manualUntil) return true;
        if (!connected)
        {
            Reset();
            return false;
        }
        if (now < suppressUntil) return false;
        if (!autoExpand)
        {
            expanded = false;
            aboveSince = belowSince = null;
            return false;
        }

        // Separate thresholds prevent rapid expand/collapse oscillation near the configured speed.
        bool high = down >= downThreshold || up >= upThreshold;
        bool low = down < downThreshold * 0.5 && up < upThreshold * 0.5;
        if (!expanded)
        {
            belowSince = null;
            if (high)
            {
                aboveSince ??= now;
                if (now - aboveSince >= triggerSeconds)
                {
                    expanded = true;
                    aboveSince = null;
                }
            }
            else aboveSince = null;
        }
        else
        {
            aboveSince = null;
            if (low)
            {
                belowSince ??= now;
                if (now - belowSince >= quietSeconds)
                {
                    expanded = false;
                    belowSince = null;
                }
            }
            else belowSince = null;
        }
        return expanded;
    }
}

public static class ScreenGeometry
{
    public static (double X, double Y) Corner(double x, double y, double width, double height,
        double windowWidth, double windowHeight, int corner, double margin = 8)
    {
        double left = corner == 0 || corner == 2 ? x + width - windowWidth - margin : x + margin;
        double top = corner == 0 || corner == 1 ? y + height - windowHeight - margin : y + margin;
        return Clamp(left, top, x, y, width, height, windowWidth, windowHeight);
    }

    public static (double X, double Y) Snap(double left, double top, double x, double y,
        double width, double height, double windowWidth, double windowHeight, double distance)
    {
        if (Math.Abs(left - x) <= distance) left = x;
        else if (Math.Abs(left + windowWidth - (x + width)) <= distance) left = x + width - windowWidth;
        if (Math.Abs(top - y) <= distance) top = y;
        else if (Math.Abs(top + windowHeight - (y + height)) <= distance) top = y + height - windowHeight;
        return (left, top);
    }

    public static (double X, double Y) Clamp(double left, double top, double x, double y,
        double width, double height, double windowWidth, double windowHeight) =>
        (Math.Clamp(left, x, Math.Max(x, x + width - windowWidth)),
         Math.Clamp(top, y, Math.Max(y, y + height - windowHeight)));
}
