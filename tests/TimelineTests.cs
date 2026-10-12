using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProxyTrafficFloat;

public static class TimelineTests
{
    static int assertions;
    static void Equal(double expected, double actual, string name)
    {
        assertions++; if (Math.Abs(expected - actual) > 0.0001) throw new Exception($"{name}: {expected} != {actual}");
    }
    static void Check(bool result, string name) { assertions++; if (!result) throw new Exception(name); }
    static Increment[] Bytes(long down, long up = 0, string target = "example.test") => new[] { new Increment(DateTime.Today, target, down, up) };
    public static void Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "TrafficLens-timeline-" + Guid.NewGuid() + ".sqlite");
        var start = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        var end = start.AddDays(1);
        try
        {
            using var store = new TrafficStore(path);
            var recorder = new TimelineRecorder(store);
            recorder.Observe(Bytes(999), 0, start, false);
            recorder.Observe(Bytes(999), 1, start.AddSeconds(1), false);
            recorder.Flush();
            Equal(0, store.QueryTimeline(start, end, "").Count, "disabled has no timeline writes");
            recorder.Observe(Bytes(999), 2, start.AddSeconds(2), true);
            recorder.Observe(Bytes(100, 30), 3, start.AddSeconds(3), true);
            recorder.Observe(Bytes(300, 10, "a'中文.test"), 4, start.AddSeconds(4), true);
            recorder.Observe(Array.Empty<Increment>(), 5, start.AddSeconds(5), true);
            recorder.Flush();
            var total = store.QueryTimeline(start, end, "").Single();
            Equal(400, total.Download, "total bytes"); Equal(40, total.Upload, "up bytes"); Equal(3, total.Seconds, "actual monitored coverage including idle");
            Check(total.First == start.AddSeconds(2).ToUnixTimeMilliseconds() && total.Last == start.AddSeconds(4).ToUnixTimeMilliseconds(), "idle time does not extend activity timestamps");
            var selected = store.QueryTimeline(start, end, "example.test").Single();
            Equal(100, selected.Download, "target selected"); Equal(3, selected.Seconds, "target uses global monitored coverage");
            Equal(300, store.QueryTimeline(start, end, "a'中文.test").Single().Download, "quoted Unicode target");
            Equal(0, store.QueryTimeline(start, end, "absent.test").Single().Download, "observed target inactivity is zero");
            Check(selected.First == start.AddSeconds(2).ToUnixTimeMilliseconds() && selected.Last == start.AddSeconds(3).ToUnixTimeMilliseconds(), "activity timestamps are observation interval");
            recorder.Flush(); Equal(400, store.QueryTimeline(start, end, "").Single().Download, "flush is not repeated");
            recorder.Observe(Bytes(900), 6, start.AddSeconds(6), false);
            recorder.Observe(Bytes(900), 7, start.AddSeconds(7), false);
            recorder.Observe(Bytes(900), 8, start.AddSeconds(8), true);
            recorder.Observe(Bytes(1), 9, start.AddSeconds(9), true); recorder.Flush();
            Equal(401, store.QueryTimeline(start, end, "").Single().Download, "re-enable establishes new baseline");
            recorder.Break();
            recorder.Observe(Bytes(99999), 120, start.AddSeconds(120), true);
            recorder.Observe(Bytes(20), 121, start.AddSeconds(121), true); recorder.Flush();
            var gapped = store.QueryTimeline(start, end, "");
            Equal(2, gapped.Count, "unmonitored minute not fabricated"); Equal(421, gapped.Sum(x => x.Download), "no outage backfill");
            Equal(1, gapped.Last().Seconds, "reconnected coverage baseline");
            recorder.Observe(Bytes(99999), 200, start.AddSeconds(200), true); recorder.Flush();
            Equal(421, store.QueryTimeline(start, end, "").Sum(x => x.Download), "sleep pause excludes history");
            store.Clear(); recorder.Clear();
            recorder.Observe(Array.Empty<Increment>(), 0, start.AddSeconds(59), true);
            recorder.Observe(Bytes(101, 3), 2, start.AddSeconds(61), true); recorder.Flush();
            var split = store.QueryTimeline(start, end, "");
            Equal(2, split.Count, "minute split"); Equal(101, split.Sum(x => x.Download), "split preserves download rounding");
            Equal(3, split.Sum(x => x.Upload), "split preserves upload rounding"); Equal(50, split.First().Download, "split proportion");
            Equal(1, split.First().Seconds, "split coverage"); Equal(51, split.Last().Download, "split remainder");
            var merged = store.QueryTimeline(start, end, "", 600).Single();
            Equal(101, merged.Download, "larger chart bucket preserves bytes"); Equal(2, merged.Minutes, "larger bucket reports observed minute count"); Equal(2, merged.Seconds, "larger bucket coverage");
            store.Add(Bytes(100)); recorder.Observe(Bytes(1000), 3, start.AddSeconds(62), true);
            store.Clear(); recorder.Clear(); recorder.Flush();
            Equal(0, store.QueryTimeline(start, end, "").Count, "clear discards pending data"); Equal(0, store.Today().Count, "clear includes daily totals");
            recorder.Observe(Array.Empty<Increment>(), 0, start, true);
            int maxBuffer = 0;
            for (int i = 1; i <= 10000; i++)
            {
                recorder.Observe(Bytes(1, 2, "target-" + i % 10), i, start.AddSeconds(i), true);
                maxBuffer = Math.Max(maxBuffer, recorder.BufferedTargets);
            }
            recorder.Flush();
            Check(maxBuffer <= 6, "five-second buffer stays bounded over 10000 samples");
            var seconds = store.QueryTimeline(start, end, "", 1);
            Equal(10000, seconds.Count, "seconds remain available after minute aggregation");
            var interval = store.QueryTimelineTotals(start.AddSeconds(100).ToUnixTimeSeconds(), start.AddSeconds(107).ToUnixTimeSeconds(), "");
            Equal(7, interval.Download, "exact raw interval download"); Equal(14, interval.Upload, "exact raw interval upload"); Equal(7, interval.Seconds, "exact interval coverage");
            var targetInterval = store.QueryTimelineTotals(start.AddSeconds(100).ToUnixTimeSeconds(),start.AddSeconds(107).ToUnixTimeSeconds(), "target-1");
            Equal(1, targetInterval.Download, "interval target filter");
            store.PruneTimeline(start.AddHours(24).AddSeconds(100));
            Equal(9900, store.QueryTimeline(start,end,"",1).Count, "rolling 24-hour retention boundary");
            Equal(9900, store.QueryTimeline(start, end, "").Sum(x => x.Download), "retained stream preserves bytes");
            store.PruneTimeline(start.AddDays(8));
            Equal(0, store.QueryTimeline(start, end, "").Count, "expired history deleted");
            store.Clear(); recorder.Clear();
            long stamp=start.ToUnixTimeSeconds();
            StorageTests.Execute(path,$"INSERT INTO timeline VALUES({stamp},'',120,30,60,{stamp*1000},{(stamp+60)*1000});INSERT INTO timeline VALUES({stamp},'legacy.test',120,30,0,{stamp*1000},{(stamp+60)*1000});");
            var legacy=store.QueryTimelineTotals(stamp,stamp+60,"legacy.test");
            Equal(120,legacy.Download,"legacy whole-minute bytes preserved"); Equal(1,legacy.LegacyMinutes,"legacy resolution retained");
            var partial=store.QueryTimelineTotals(stamp+10,stamp+70,"legacy.test");
            Equal(0,partial.Download,"legacy partial minute not fabricated"); Equal(1,partial.PartialLegacyMinutes,"partial precision warning available");
            Equal(60,store.QueryTimeline(start,start.AddMinutes(1),"",1).Single().Resolution,"zoom does not invent legacy seconds");
            store.Clear(); recorder.Clear();
            StorageTests.Execute(path,"CREATE TRIGGER reject_history BEFORE UPDATE ON timeline_seconds BEGIN SELECT RAISE(ABORT,'fixture failure'); END;");
            recorder.Observe(Array.Empty<Increment>(),0,start,true);
            bool failed=false;
            try { for(int i=1;i<=5;i++) recorder.Observe(Bytes(1),i,start.AddSeconds(i),true); } catch(IOException) { failed=true; }
            Check(failed&&recorder.BufferedSamples==0,"failed history transaction cannot retain an unbounded buffer");
            Equal(0,store.QueryTimeline(start,end,"").Count,"failed history transaction rolls back all rows");
            StorageTests.Execute(path,"DROP TRIGGER reject_history;");
            recorder.Observe(Bytes(7),6,start.AddSeconds(6),true); recorder.Flush();
            Equal(7,store.QueryTimeline(start,end,"").Single().Download,"history writer recovers after failed batch without backfill");
            var smooth=ChartMath.Smooth(new[] { new CurvePoint(0,0), new CurvePoint(1,100), new CurvePoint(2,5), new CurvePoint(3,0), new CurvePoint(double.NaN,double.NaN), new CurvePoint(5,10) });
            Check(smooth.Where(p=>double.IsFinite(p.Y)).All(p=>p.Y>=0 && p.Y<=100), "smoothing never invents peaks or negative values");
            Check(smooth.Any(p=>double.IsNaN(p.X)) && smooth.Last().X==5, "smoothing preserves gaps and endpoints");
            Check(ChartMath.Step(86400,false)>=72 && ChartMath.Step(60,false)==1, "zoom aggregation bounds point count");
            Console.WriteLine($"PASS: {assertions} timeline assertions (toggle, coverage, timestamps, gaps, split, filtering, clear, 10000-sample bounded buffer)");
        }
        finally { foreach (string suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
    }
}
