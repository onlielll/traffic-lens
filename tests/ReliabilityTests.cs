using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using ProxyTrafficFloat;

public static class ReliabilityTests
{
    public static void Worker(string path)
    {
        for (int i = 0; i < 100; i++)
            Settings.Update(path, value => value.Left++);
    }

    public static async Task Run()
    {
        var types = new Dictionary<string, string> { ["proxy"] = "Shadowsocks" };
        string Entry(string id, int bytes, string extra = "") =>
            "{\"id\":\"" + id + "\",\"chains\":[\"proxy\"],\"metadata\":{\"host\":\"example.test\"},\"download\":" + bytes + ",\"upload\":0" + extra + "}";
        Snapshot Parse(string records) => ConnectionParser.Parse("{\"connections\":[" + records + "]}", types);
        var engine = new TrafficEngine();
        engine.Sample(Parse(Entry("good", 0)), 0, DateTime.Today);
        var mixed = Parse(Entry("good", 100) + ",{\"id\":\"bad\",\"chains\":[\"proxy\"],\"download\":50,\"upload\":0},null");
        if (mixed.Connections.Count != 1 || mixed.UnknownCount != 2 ||
            engine.Sample(mixed, 1, DateTime.Today.AddSeconds(1)).Sum(x => x.Download) != 100)
            throw new Exception("Malformed connection affected healthy baselines.");
        if (Parse(Entry("bad", 1, ",\"download\":\"bad\"")).Connections.Count != 0)
            throw new Exception("Invalid counter became a zero baseline.");
        if (Parse(Entry("bad", -1)).Connections.Count != 0)
            throw new Exception("Negative counter accepted.");
        foreach (string invalid in new[] { "{}", "null", "{\"connections\":null}", "{\"connections\":{}}" })
        {
            bool failed = false;
            try { ConnectionParser.Parse(invalid, types); } catch (JsonException) { failed = true; }
            if (!failed) throw new Exception("Invalid whole response was treated as success.");
        }
        if (ConnectionParser.ParseProxyTypes("{\"proxies\":{\"a\":{\"type\":\"Http\"},\"a\":{\"type\":\"Vless\"}}}")["a"] != "Vless")
            throw new Exception("Duplicate proxy fields failed.");

        string root = Path.Combine(Path.GetTempPath(), "TrafficLens-reliability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string nullablePath = Path.Combine(root, "nullable-settings.json");
            File.WriteAllText(nullablePath, "{\"LatencyNode\":null,\"Endpoint\":null}");
            var nullable = Settings.Load(nullablePath);
            if (nullable.LatencyNode != "" || nullable.Endpoint != "")
                throw new Exception("Null local settings reached sampling/latency consumers.");
            File.WriteAllText(nullablePath, "{\"LatencyNode\":\" example-node \"}");
            if (Settings.Load(nullablePath).LatencyNode != "example-node")
                throw new Exception("Node selection not normalized at the settings boundary.");
            string path = Path.Combine(root, "settings.json"), broken = "{\"Visible\":false,broken";
            File.WriteAllText(path, broken);
            var recovered = Settings.Load(path);
            string backup = Directory.GetFiles(root, "settings.json.corrupt-*").Single();
            if (!recovered.Recovered || File.ReadAllText(backup) != broken)
                throw new Exception("Corrupt configuration not preserved.");
            recovered.Save(path);
            if (Settings.Load(path).Recovered || !Settings.Load(path).Visible)
                throw new Exception("Recovered configuration cannot be saved.");
            for (int i = 0; i < 6; i++)
            {
                File.WriteAllText(path, "{broken-" + i);
                if (!Settings.Load(path).Recovered) throw new Exception("Corrupt settings recovery failed.");
            }
            var retained = Directory.GetFiles(root, "settings.json.corrupt-*").OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (retained.Length != 3 || File.ReadAllText(retained[^1]) != "{broken-5")
                throw new Exception("Corrupt settings backups are not bounded or discarded the newest evidence.");
            string lockedCorrupt = Path.Combine(root, "locked-corrupt.json");
            for (int i = 0; i < 3; i++) { File.WriteAllText(lockedCorrupt, "{broken"); Settings.Load(lockedCorrupt); }
            string oldest = Directory.GetFiles(root, "locked-corrupt.json.corrupt-*").OrderBy(x => x, StringComparer.Ordinal).First();
            using (new FileStream(oldest, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                File.WriteAllText(lockedCorrupt, "{broken");
                if (!Settings.Load(lockedCorrupt).Recovered) throw new Exception("Locked old backup prevented completed recovery.");
            }
            File.WriteAllText(lockedCorrupt, "{broken");
            Settings.Load(lockedCorrupt);
            if (Directory.GetFiles(root, "locked-corrupt.json.corrupt-*").Length != 3)
                throw new Exception("Backup cleanup did not resume after the file was released.");
            recovered.Save(path);
            // Two real processes contend on the complete read-modify-write transaction.
            Process StartWorker()
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
                    start.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
                start.ArgumentList.Add("--settings-worker");
                start.ArgumentList.Add(path);
                return Process.Start(start)!;
            }
            using var first = StartWorker();
            using var second = StartWorker();
            await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(20));
            if (first.ExitCode != 0 || second.ExitCode != 0 || Settings.Load(path).Left != 232)
                throw new Exception("Cross-process configuration update lost edits.");
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                bool blocked = false;
                try { Settings.Load(path); } catch (IOException) { blocked = true; }
                if (!blocked) throw new Exception("Unreadable settings silently became defaults.");
            }

            string log = Directory.GetFiles(Path.Combine(root, "logs"), "*.log").Single();
            File.WriteAllText(log, new string('x', 256 * 1024));
            var error = new IOException("private-api-secret https://private.example/full/path");
            Diagnostics.Write(root, "rotation-test", error);
            Diagnostics.Write(root, "rotation-test", error);
            if (!File.Exists(log + ".old") || File.ReadAllLines(log).Length != 1 ||
                File.ReadAllText(log).Contains("private-api-secret") || File.ReadAllText(log).Contains("private.example"))
                throw new Exception("Diagnostic bounds, deduplication or privacy failed.");
        }
        finally { Directory.Delete(root, true); }

        var stable = new ResourceTrend();
        for (int i = 0; i < 10000; i++) stable.Add(25 * 1024 * 1024 + i % 2 * 1024);
        if (stable.Stable != true) throw new Exception("Stable resource probe failed.");
        var growth = new ResourceTrend();
        for (int i = 0; i < 10; i++) growth.Add((i < 5 ? 25 : 75) * 1024 * 1024);
        if (growth.Stable != false) throw new Exception("Resource growth probe did not fail.");
        if (new ResourceTrend().Stable != null) throw new Exception("Insufficient samples claimed validation.");
        Console.WriteLine("PASS: malformed-record isolation, schema/counters, corrupt backup, 200 cross-process updates, unreadable settings, bounded private diagnostics, resource trend");
    }
}
