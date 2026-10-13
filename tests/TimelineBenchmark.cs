using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProxyTrafficFloat;

public static class TimelineBenchmark
{
    public static void Run()
    {
        foreach(var scenario in new[] { (0,1800), (5,86400), (20,1800) })
        {
            string path=Path.Combine(Path.GetTempPath(),"TrafficLens-benchmark-"+Guid.NewGuid()+".sqlite");
            int targets=scenario.Item1, samples=scenario.Item2;
            var start=DateTimeOffset.UtcNow.AddSeconds(-samples);
            var watch=new Stopwatch(); var flushTimes=new List<double>(); double query, targetQuery; int maxBuffer=0, pointCount=0;
            try
            {
                using(var store=new TrafficStore(path))
                {
                    var recorder=new TimelineRecorder(store);
                    recorder.Observe(Array.Empty<Increment>(),0,start,true);
                    for(int i=1;i<=samples;i++)
                    {
                        var increments=Enumerable.Range(0,targets).Select(j=>new Increment(DateTime.Today,"target-"+j+".example",1024*(j+1),128)).ToArray();
                        watch.Restart(); recorder.Observe(increments,i,start.AddSeconds(i),true); watch.Stop();
                        if(i%5==0) flushTimes.Add(watch.Elapsed.TotalMilliseconds);
                        maxBuffer=Math.Max(maxBuffer,recorder.BufferedSamples);
                    }
                    recorder.Flush();
                    watch.Restart(); var data=store.QueryTimeline(start,start.AddSeconds(samples+1),"",ChartMath.Step(samples,false)); watch.Stop(); query=watch.Elapsed.TotalMilliseconds; pointCount=data.Count;
                    watch.Restart(); store.QueryTimeline(start,start.AddSeconds(samples+1),"target-0.example",ChartMath.Step(samples,false)); watch.Stop(); targetQuery=watch.Elapsed.TotalMilliseconds;
                    if(data.Sum(p=>p.Download)!=(long)samples*1024*targets*(targets+1)/2) throw new Exception("Benchmark dropped bytes.");
                    store.PruneTimeline(start.AddSeconds(samples+1));
                }
                double size=new FileInfo(path).Length/1048576.0;
                flushTimes.Sort();
                Console.WriteLine($"targets={targets}, seconds={samples}, dbMiB={size:F2}, projected24hMiB={size*86400/samples:F2}, flushMeanMs={flushTimes.Average():F2}, flushP95Ms={flushTimes[(int)(flushTimes.Count*0.95)]:F2}, totalQueryMs={query:F2}, targetQueryMs={targetQuery:F2}, points={pointCount}, maxBufferedRows={maxBuffer}");
            }
            finally { foreach(string suffix in new[]{"","-wal","-shm"}) if(File.Exists(path+suffix)) File.Delete(path+suffix); }
        }
    }
}
