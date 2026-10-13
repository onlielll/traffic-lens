using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using ProxyTrafficFloat;

static class FeatureTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Run()
    {
        Check(ScreenGeometry.CoversScreen(-1920, 0, 0, 1080, -1920, 0, 0, 1080), "Negative-coordinate fullscreen");
        Check(!ScreenGeometry.CoversScreen(0, 0, 1920, 1040, 0, 0, 1920, 1080), "Maximized with taskbar isn't fullscreen");
        string history = JsonSerializer.Serialize(new { history = Enumerable.Range(0, 120).Select(i => new { delay = 350 + i, time = DateTimeOffset.UtcNow.ToString("O") }) });
        var node = NodeLatency.Parse(history, "example-node", 300)!;
        Check(node.Samples.Length == 60 && node.Delay == 469 && node.Slow && node.Fresh, "Bounded latency history and consecutive slow tests");
        Check(NodeLatency.Parse("{\"history\":[]}", "node", 300) == null, "Missing test data");
        var invalid = NodeLatency.Parse("{\"history\":[null,{\"delay\":\"bad\",\"time\":\"bad\"}]}", "node", 300)!;
        Check(invalid.Delay == 0 && !invalid.Slow && !invalid.Fresh, "Malformed history is unavailable");
        string releases = "[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v2.2.0-preview\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.2.0-preview\"},{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.1.1\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.1.1\"}]";
        Check(ReleaseCheck.Parse(releases, false)!.Version == new Version(2, 1, 1), "Stable updates exclude previews");
        Check(ReleaseCheck.Parse(releases, true)!.Version == new Version(2, 2, 0), "Preview opt-in");
        Check(ReleaseCheck.Parse("[{\"draft\":true},{\"tag_name\":\"bad\"}]", true) == null, "Draft/malformed release excluded");
        Check(ReleaseCheck.Parse(releases.Replace("https://github.com/", "https://example.com/"), true) == null, "Only this repository's release pages open");
        string root = Path.Combine(Path.GetTempPath(), "TrafficLens-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new TrafficStore(Path.Combine(root, "live.sqlite"));
            store.Add(new[] { new Increment(DateTime.Today, "=SUM(1,2)", 100, 20), new Increment(DateTime.Today, "a,\"b.test", 50, 5) });
            string csv = Path.Combine(root, "usage.csv");
            store.ExportCsv(csv, false);
            string text = File.ReadAllText(csv);
            Check(text.Contains("\"'=SUM(1,2)\"") && text.Contains("\"a,\"\"b.test\""), "CSV quotes and spreadsheet formula safety");
            var cancelled = new CancellationToken(true);
            try { store.ExportCsv(csv, false, cancelled); throw new Exception("Expected cancelled export"); } catch (OperationCanceledException) { }
            Check(File.ReadAllText(csv) == text && !Directory.GetFiles(root, "*.tmp").Any(), "Cancellation preserves existing export and removes partial output");
            string backup = Path.Combine(root, "backup.sqlite");
            store.Backup(backup);
            store.Add(new[] { new Increment(DateTime.Today, "later.test", 99, 1) });
            using var restored = new TrafficStore(backup);
            Check(restored.ObservedBytes() == 175 && restored.Query(DateTime.Today, DateTime.Today).Count == 2, "Online backup includes committed WAL data and remains an independent snapshot");
            store.ExportCsv(Path.Combine(root, "empty-timeline.csv"), true);
            Check(File.ReadAllLines(Path.Combine(root, "empty-timeline.csv")).Length == 1, "Empty timeline export");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: fullscreen geometry, bounded latency, slow/stale results, release filtering, online WAL backup, streaming/formula-safe CSV, cancellation");
    }
}