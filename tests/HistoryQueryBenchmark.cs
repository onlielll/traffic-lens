using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProxyTrafficFloat;

static class HistoryQueryBenchmark
{
    public static void Run()
    {
        string path=Path.Combine(Path.GetTempPath(),"TrafficLens-history-benchmark-"+Guid.NewGuid()+".sqlite");
        DateTime last=DateTime.Today, first=last.AddDays(-6);
        try
        {
            using(var store=new TrafficStore(path))
                for(int day=0;day<7;day++)
                    store.Add(Enumerable.Range(0,20000).Select(i=>new Increment(last.AddDays(-day),"domain-"+i+".example",i*11L+1,i*3L)));
            using var reader=TrafficStore.OpenReadOnly(path);
            reader.Query(first,last); reader.Query(first,last,200);
            foreach(int limit in new[]{-1,200})
            {
                var times=new double[7]; var bytes=new long[7]; int rows=0;
                for(int i=0;i<7;i++)
                {
                    long before=GC.GetAllocatedBytesForCurrentThread(); var watch=Stopwatch.StartNew();
                    var result=reader.Query(first,last,limit); watch.Stop();
                    bytes[i]=GC.GetAllocatedBytesForCurrentThread()-before; times[i]=watch.Elapsed.TotalMilliseconds; rows=result.Count;
                }
                Array.Sort(times); Array.Sort(bytes);
                Console.WriteLine($"limit={limit}, rows={rows}, medianMs={times[3]:F3}, allocatedBytes={bytes[3]}, fixtureRows=140000, domains=20000");
            }
        }
        finally {foreach(string suffix in new[]{"","-wal","-shm"}) if(File.Exists(path+suffix)) File.Delete(path+suffix);}
    }
}
