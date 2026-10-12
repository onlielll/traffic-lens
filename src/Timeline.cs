using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ProxyTrafficFloat;

public record TimelinePoint(long Minute, long Download, long Upload, double Seconds, int Minutes, long First, long Last);
public sealed class TimelineBucket
{
    public long Minute, Download, Upload, First, Last;
    public double Seconds;
    public string Target;
    public TimelineBucket(long minute, string target) { Minute = minute; Target = target; }
    public void Add(long down, long up, double seconds, long first, long last)
    {
        Download += down; Upload += up; Seconds += seconds;
        if (down != 0 || up != 0)
        {
            First = First == 0 ? first : Math.Min(First, first);
            Last = Math.Max(Last, last);
        }
    }
}

/// <summary>Owns only the current minute. Uses the resident's existing sample stream, not another timer.</summary>
public sealed class TimelineRecorder
{
    readonly TrafficStore store;
    readonly Dictionary<string, TimelineBucket> pending = new(StringComparer.Ordinal);
    long minute;
    bool recording;
    double? previousTime;

    public TimelineRecorder(TrafficStore store) => this.store = store;
    public int BufferedTargets => pending.Count;

    public void Observe(IEnumerable<Increment> increments, double time, DateTimeOffset utc, bool enabled)
    {
        if (recording != enabled)
        {
            Flush(); previousTime = null; recording = enabled;
        }
        double? before = previousTime;
        previousTime = enabled ? time : null;
        if (!enabled || before == null) return;
        double elapsed = time - before.Value;
        if (elapsed <= 0 || elapsed > 5) { Flush(); return; }

        // Anchor at the observation time; elapsed time remains monotonic even if the clock is adjusted.
        var end = utc.ToUniversalTime();
        var start = end.AddSeconds(-elapsed);
        var amounts = new Dictionary<string, Amount>(StringComparer.Ordinal);
        foreach (var increment in increments)
        {
            if (increment.Download == 0 && increment.Upload == 0) continue;
            amounts.TryGetValue(increment.Target, out var old);
            amounts[increment.Target] = new((old?.Download ?? 0) + increment.Download, (old?.Upload ?? 0) + increment.Upload);
        }
        var remaining = new Dictionary<string, Amount>(amounts, StringComparer.Ordinal);
        for (var cursor = start; cursor < end;)
        {
            long key = cursor.ToUnixTimeSeconds() / 60 * 60;
            if (pending.Count > 0 && minute != key) Flush();
            minute = key;
            var next = DateTimeOffset.FromUnixTimeSeconds(key + 60);
            if (next > end) next = end;
            double seconds = (next - cursor).TotalSeconds;
            long down = 0, up = 0;
            foreach (var pair in amounts)
            {
                var left = remaining[pair.Key];
                long d = next == end ? left.Download : (long)(pair.Value.Download * seconds / elapsed);
                long u = next == end ? left.Upload : (long)(pair.Value.Upload * seconds / elapsed);
                remaining[pair.Key] = new(left.Download - d, left.Upload - u);
                if (d != 0 || u != 0) Bucket(pair.Key).Add(d, u, 0, cursor.ToUnixTimeMilliseconds(), next.ToUnixTimeMilliseconds());
                down += d; up += u;
            }
            // The empty target is the coverage/total row; per-target idle time need not be stored repeatedly.
            Bucket("").Add(down, up, seconds, cursor.ToUnixTimeMilliseconds(), next.ToUnixTimeMilliseconds());
            cursor = next;
        }
        TimelineBucket Bucket(string target)
        {
            if (!pending.TryGetValue(target, out var bucket)) pending.Add(target, bucket = new(minute, target));
            return bucket;
        }
    }

    public void Flush()
    {
        if (pending.Count == 0) return;
        store.AddTimeline(pending.Values);
        pending.Clear();
    }
    public void Break() { previousTime = null; Flush(); }
    public void Clear() { pending.Clear(); previousTime = null; }
}

public sealed partial class TrafficStore
{
    public void AddTimeline(IEnumerable<TimelineBucket> buckets)
    {
        var sql = new StringBuilder("BEGIN IMMEDIATE;");
        foreach (var row in buckets)
        {
            string target = Quote(row.Target), seconds = row.Seconds.ToString("R", CultureInfo.InvariantCulture);
            sql.Append($"INSERT OR IGNORE INTO timeline VALUES({row.Minute},{target},0,0,0,0,0);");
            sql.Append($"UPDATE timeline SET download=download+{row.Download},upload=upload+{row.Upload},seconds=seconds+{seconds}," +
                $"first=CASE WHEN first=0 THEN {row.First} ELSE MIN(first,{row.First}) END,last=MAX(last,{row.Last}) " +
                $"WHERE minute={row.Minute} AND target={target};");
        }
        sql.Append("COMMIT;");
        Transaction(sql.ToString());
    }

    public List<TimelinePoint> QueryTimeline(DateTimeOffset first, DateTimeOffset last, string target, int stepSeconds = 60)
    {
        var rows = new List<TimelinePoint>();
        Callback callback = (_, _, values, _) =>
        {
            try
            {
                string Cell(int i) => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, i * IntPtr.Size)) ?? "0";
                rows.Add(new(long.Parse(Cell(0)), long.Parse(Cell(1)), long.Parse(Cell(2)),
                    double.Parse(Cell(3), CultureInfo.InvariantCulture), int.Parse(Cell(4)), long.Parse(Cell(5)), long.Parse(Cell(6))));
                return 0;
            }
            catch { return 1; }
        };
        string source = target.Length == 0 ? "timeline c" : $"timeline c LEFT JOIN timeline t ON t.minute=c.minute AND t.target={Quote(target)}";
        string data = target.Length == 0 ? "c" : "t";
        Exec($"SELECT c.minute/{stepSeconds}*{stepSeconds},COALESCE(SUM({data}.download),0),COALESCE(SUM({data}.upload),0)," +
            $"SUM(c.seconds),COUNT(*),COALESCE(MIN(NULLIF({data}.first,0)),0),COALESCE(MAX({data}.last),0) FROM {source} " +
            $"WHERE c.target='' AND c.minute>={first.ToUnixTimeSeconds()} AND c.minute<{last.ToUnixTimeSeconds()} " +
            $"GROUP BY c.minute/{stepSeconds} ORDER BY c.minute/{stepSeconds};", callback);
        GC.KeepAlive(callback);
        return rows;
    }
    public void PruneTimeline(DateTimeOffset now) => Exec($"DELETE FROM timeline WHERE minute<{now.AddDays(-7).ToUnixTimeSeconds() / 60 * 60};");
}
