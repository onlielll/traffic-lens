using System;
using System.IO;
using System.Linq;
using ProxyTrafficFloat;

public static class ForecastTests
{
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "TrafficLens-forecast-" + Guid.NewGuid() + ".sqlite");
        const long GB = 1_000_000_000;
        try
        {
            StorageTests.Execute(path, "CREATE TABLE daily(day TEXT NOT NULL,target TEXT NOT NULL,download INTEGER NOT NULL,upload INTEGER NOT NULL,PRIMARY KEY(day,target));" +
                "INSERT INTO daily VALUES('2020-01-01','old.test',10,3);");
            using (var store = new TrafficStore(path))
            {
                if (store.ObservedBytes() != 13 || store.RecentDailyTrend(DateTime.Today).Days != 0)
                    throw new Exception("Migration did not seed the scalar counter before pruning.");
                for (int i = 1; i <= 9; i++)
                    store.Add(new[] { new Increment(DateTime.Today.AddDays(-i), "example.test", i * GB / 2, i * GB / 2) });
                store.Add(new[] { new Increment(DateTime.Today, "today.test", 100 * GB, 0) });
                var trend = store.RecentDailyTrend(DateTime.Today);
                if (trend.Days != 7 || trend.DailyBytes != 4 * GB)
                    throw new Exception("Daily forecast included today, omitted uploads, or selected the wrong days.");
                long baseline = store.ObservedBytes();
                var config = new Settings { QuotaGB = 48, QuotaBaselineBytes = baseline, QuotaUpdatedUtc = DateTime.UtcNow };
                store.Add(new[] { new Increment(DateTime.Today, "today.test", 5 * GB, GB) });
                var estimate = QuotaForecast.Calculate(config, store.ObservedBytes(), trend)!.Value;
                if (estimate.RemainingBytes != 42 * GB || estimate.Days != 10.5)
                    throw new Exception("Allowance did not subtract only observed usage since its update.");
                StorageTests.Execute(path, "CREATE TRIGGER counter_failure BEFORE UPDATE ON usage_counter BEGIN SELECT RAISE(ABORT,'fixture'); END;");
                bool failed = false;
                try { store.Add(new[] { new Increment(DateTime.Today, "failed.test", GB, 0) }); } catch (IOException) { failed = true; }
                if (!failed || store.ObservedBytes() != baseline + 6 * GB || store.Today().ContainsKey("failed.test"))
                    throw new Exception("Daily rows and allowance counter did not roll back together.");
                StorageTests.Execute(path, "DROP TRIGGER counter_failure;");
                store.Clear();
                if (store.ObservedBytes() != baseline + 6 * GB || store.RecentDailyTrend(DateTime.Today).Days != 0 ||
                    QuotaForecast.Calculate(config, store.ObservedBytes(), default)!.Value.RemainingBytes != 42 * GB)
                    throw new Exception("Clearing history restored spent allowance.");
                store.Add(new[] { new Increment(DateTime.Today, "today.test", GB, 0) });
                if (store.RecentDailyTrend(DateTime.Today).Days != 0)
                    throw new Exception("A partial first day produced a daily forecast.");
                string settings = path + ".json";
                config.Save(settings);
                if (!Settings.Load(settings).HasQuota || Settings.Load(settings).QuotaBaselineBytes != baseline)
                    throw new Exception("Allowance snapshot did not survive settings persistence.");
                File.Delete(settings);
            }
            using (var reopened = new TrafficStore(path))
                if (reopened.ObservedBytes() != 152 * GB + 13) throw new Exception("Restart reseeded the cumulative counter.");
            if (QuotaForecast.Calculate(new Settings(), 0, default) != null ||
                new QuotaForecast(GB, 0, 0).Days != null || new QuotaForecast(0, GB, 7).Days != 0)
                throw new Exception("Unset, empty or exhausted forecast is incorrect.");
            Console.WriteLine("PASS: allowance migration, last seven recorded days, upload billing, incremental subtraction, atomic rollback, clear/restart, missing history, persistence");
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm", ".json" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }
}