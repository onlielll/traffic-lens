using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ProxyTrafficFloat;

public record TimelinePoint(long Timestamp, long Download, long Upload, double Seconds, int Minutes, long First, long Last, int Resolution);
public record TimelineTotals(long Download, long Upload, double Seconds, int LegacyMinutes, int PartialLegacyMinutes);
public sealed class TimelineBucket
{
    public long Timestamp, Download, Upload, First, Last;
    public double Seconds;
    public string Target;
    public TimelineBucket(long timestamp, string target)
    {
        Timestamp = timestamp;
        Target = target;
    }
    public void Add(long down, long up, double seconds, long first, long last)
    {
        Download += down;
        Upload += up;
        Seconds += seconds;
        if (down != 0 || up != 0)
        {
            First = First == 0 ? first : Math.Min(First, first);
            Last = Math.Max(Last, last);
        }
    }
}

/// <summary>Reuses the existing sampler; owns only a five-second batch, never a day of history.</summary>
public sealed class TimelineRecorder
{
    readonly TrafficStore store;
    readonly Dictionary<(long Time, string Target), TimelineBucket> pending = new();
    bool recording;
    double? previousTime;
    double lastFlush;
    public TimelineRecorder(TrafficStore store) => this.store = store;
    public int BufferedTargets => pending.Values.Select(row => row.Target).Distinct().Count();
    public int BufferedSamples => pending.Count;

    public void Observe(IEnumerable<Increment> increments, double time, DateTimeOffset utc, bool enabled)
    {
        if (recording != enabled)
        {
            Flush();
            previousTime = null;
            recording = enabled;
            lastFlush = time;
        }
        double? before = previousTime;
        previousTime = enabled ? time : null;
        if (!enabled || before == null)
        {
            lastFlush = time;
            return;
        }
        double elapsed = time - before.Value;
        if (elapsed <= 0 || elapsed > 5)
        {
            Flush();
            lastFlush = time;
            return;
        }
        var end = utc.ToUniversalTime();
        var start = end.AddSeconds(-elapsed);
        var amounts = new Dictionary<string, Amount>(StringComparer.Ordinal);
        foreach (var increment in increments)
        {
            if (increment.Download == 0 && increment.Upload == 0)
                continue;
            amounts.TryGetValue(increment.Target, out var old);
            amounts[increment.Target] = new((old?.Download ?? 0) + increment.Download, (old?.Upload ?? 0) + increment.Upload);
        }
        var remaining = new Dictionary<string, Amount>(amounts, StringComparer.Ordinal);
        for (var cursor = start; cursor < end;)
        {
            long key = cursor.ToUnixTimeSeconds();
            var next = DateTimeOffset.FromUnixTimeSeconds(key + 1);
            if (next > end)
                next = end;
            double seconds = (next - cursor).TotalSeconds;
            long down = 0, up = 0;
            foreach (var pair in amounts)
            {
                var left = remaining[pair.Key];
                long d = next == end ? left.Download : (long)(pair.Value.Download * seconds / elapsed);
                long u = next == end ? left.Upload : (long)(pair.Value.Upload * seconds / elapsed);
                remaining[pair.Key] = new(left.Download - d, left.Upload - u);
                if (d != 0 || u != 0)
                    Bucket(key, pair.Key).Add(d, u, 0, cursor.ToUnixTimeMilliseconds(), next.ToUnixTimeMilliseconds());
                down += d;
                up += u;
            }
            Bucket(key, "").Add(down, up, seconds, cursor.ToUnixTimeMilliseconds(), next.ToUnixTimeMilliseconds());
            cursor = next;
        }
        if (time - lastFlush >= 5)
        {
            Flush();
            lastFlush = time;
        }
    }
    TimelineBucket Bucket(long time, string target)
    {
        var key = (time, target);
        if (!pending.TryGetValue(key, out var bucket))
            pending.Add(key, bucket = new(time, target));
        return bucket;
    }
    public void Flush()
    {
        if (pending.Count == 0)
            return;
        // A failed batch is a gap, not an unbounded retry queue. The monitor reports the save error.
        try
        {
            store.AddTimeline(pending.Values);
        }
        finally { pending.Clear(); }
    }
    public void Break()
    {
        previousTime = null;
        Flush();
    }
    public void Clear()
    {
        pending.Clear();
        previousTime = null;
    }
}

public sealed partial class TrafficStore
{
    public void AddTimeline(IEnumerable<TimelineBucket> buckets)
    {
        var data = buckets.ToList();
        var sql = new StringBuilder("BEGIN IMMEDIATE;");
        foreach (string name in data.Where(row => row.Target.Length > 0).Select(row => row.Target).Distinct())
            sql.Append($"INSERT OR IGNORE INTO timeline_names(target) VALUES({Quote(name)});");
        foreach (var row in data)
        {
            if (row.Target.Length == 0)
            {
                sql.Append($"INSERT OR IGNORE INTO timeline_seconds VALUES({row.Timestamp},0,0,0);");
                sql.Append($"UPDATE timeline_seconds SET download=download+{row.Download},upload=upload+{row.Upload}," +
                    $"seconds=seconds+{row.Seconds.ToString("R", CultureInfo.InvariantCulture)} WHERE stamp={row.Timestamp};");
            }
            else
            {
                string id = $"(SELECT id FROM timeline_names WHERE target={Quote(row.Target)})";
                sql.Append($"INSERT OR IGNORE INTO timeline_target_seconds VALUES({row.Timestamp},{id},0,0);");
                sql.Append($"UPDATE timeline_target_seconds SET download=download+{row.Download},upload=upload+{row.Upload} " +
                    $"WHERE stamp={row.Timestamp} AND target_id={id};");
            }
        }
        sql.Append("COMMIT;");
        Transaction(sql.ToString());
    }

    // Old minute records retain their precision; new records never reconstruct missing seconds.
    string TimelineSource(string target, long first, long last, bool wholeLegacyMinutes = false)
    {
        string id = $"(SELECT id FROM timeline_names WHERE target={Quote(target)})";
        string source = target.Length == 0 ? "timeline_seconds c" : $"timeline_seconds c LEFT JOIN timeline_target_seconds t ON t.stamp=c.stamp AND t.target_id={id}";
        string value = target.Length == 0 ? "c" : "t";
        string legacySource = target.Length == 0 ? "timeline c" : $"timeline c LEFT JOIN timeline t ON t.minute=c.minute AND t.target={Quote(target)}";
        string legacyEnd = wholeLegacyMinutes ? $"c.minute+60<={last}" : $"c.minute<{last}";
        return $"SELECT c.stamp AS stamp,COALESCE({value}.download,0) AS download,COALESCE({value}.upload,0) AS upload,c.seconds," +
            $"CASE WHEN COALESCE({value}.download,0)+COALESCE({value}.upload,0)>0 THEN c.stamp*1000 ELSE 0 END AS first," +
            $"CASE WHEN COALESCE({value}.download,0)+COALESCE({value}.upload,0)>0 THEN (c.stamp+1)*1000 ELSE 0 END AS last,1 AS resolution " +
            $"FROM {source} WHERE c.stamp>={first} AND c.stamp<{last} UNION ALL " +
            $"SELECT c.minute,COALESCE({value}.download,0),COALESCE({value}.upload,0),c.seconds,COALESCE({value}.first,0),COALESCE({value}.last,0),60 " +
            $"FROM {legacySource} WHERE c.target='' AND c.minute>={first} AND {legacyEnd}";
    }

    static string Cell(IntPtr values, int i) => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(values, i * IntPtr.Size)) ?? "0";
    public List<TimelinePoint> QueryTimeline(DateTimeOffset first, DateTimeOffset last, string target, int stepSeconds = 60)
    {
        var rows = new List<TimelinePoint>();
        Callback callback = (_, _, values, _) =>
        {
            try
            {
                rows.Add(new(long.Parse(Cell(values, 0)), long.Parse(Cell(values, 1)), long.Parse(Cell(values, 2)),
                    double.Parse(Cell(values, 3), CultureInfo.InvariantCulture), int.Parse(Cell(values, 4)), long.Parse(Cell(values, 5)),
                    long.Parse(Cell(values, 6)), int.Parse(Cell(values, 7))));
                return 0;
            }
            catch { return 1; }
        };
        string raw = TimelineSource(target, first.ToUnixTimeSeconds(), last.ToUnixTimeSeconds());
        Exec($"SELECT stamp/{stepSeconds}*{stepSeconds},SUM(download),SUM(upload),SUM(seconds),COUNT(DISTINCT stamp/60)," +
            $"COALESCE(MIN(NULLIF(first,0)),0),MAX(last),MAX(resolution) FROM ({raw}) GROUP BY stamp/{stepSeconds} ORDER BY stamp/{stepSeconds};", callback);
        GC.KeepAlive(callback);
        return rows;
    }

    public TimelineTotals QueryTimelineTotals(long first, long last, string target)
    {
        TimelineTotals total = new(0, 0, 0, 0, 0);
        Callback callback = (_, _, values, _) =>
        {
            try
            {
                total = new(long.Parse(Cell(values, 0)), long.Parse(Cell(values, 1)), double.Parse(Cell(values, 2), CultureInfo.InvariantCulture),
                    int.Parse(Cell(values, 3)), int.Parse(Cell(values, 4)));
                return 0;
            }
            catch { return 1; }
        };
        string partial = $"SELECT COUNT(*) FROM timeline WHERE target='' AND minute<{last} AND minute+60>{first} AND NOT(minute>={first} AND minute+60<={last})";
        Exec($"SELECT COALESCE(SUM(download),0),COALESCE(SUM(upload),0),COALESCE(SUM(seconds),0)," +
            $"COALESCE(SUM(CASE WHEN resolution=60 THEN 1 ELSE 0 END),0),({partial}) FROM ({TimelineSource(target, first, last, true)});", callback);
        GC.KeepAlive(callback);
        return total;
    }

    public void PruneTimeline(DateTimeOffset now)
    {
        long cutoff = now.AddHours(-24).ToUnixTimeSeconds();
        Transaction($"BEGIN IMMEDIATE;DELETE FROM timeline_target_seconds WHERE stamp<{cutoff};DELETE FROM timeline_seconds WHERE stamp<{cutoff};" +
            $"DELETE FROM timeline WHERE minute+60<={cutoff};DELETE FROM timeline_names WHERE NOT EXISTS " +
            "(SELECT 1 FROM timeline_target_seconds WHERE target_id=timeline_names.id);COMMIT;");
    }
}
