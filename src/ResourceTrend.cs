using System;

namespace ProxyTrafficFloat;

// Explicit soak tests only: five post-warmup baseline samples and a five-sample rolling tail.
public sealed class ResourceTrend
{
    readonly long[] recent = new long[5];
    long baseline;
    int count;
    public void Add(long bytes)
    {
        if (count < recent.Length) baseline += bytes;
        recent[count % recent.Length] = bytes;
        count++;
    }
    public long Growth
    {
        get
        {
            long sum = 0;
            foreach (long value in recent) sum += value;
            return count < 10 ? 0 : (sum - baseline) / recent.Length;
        }
    }
    public bool? Stable => count < 10 ? null : Growth <= Math.Max(16 * 1024 * 1024, baseline / recent.Length * 0.35);
}
