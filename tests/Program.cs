using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using ProxyTrafficFloat;

static class Test
{
    static int assertions;
    static void Equal(double expected, double actual, string message) { assertions++; if (Math.Abs(expected - actual) > 0.0001) throw new Exception($"{message}: expected {expected}, got {actual}"); }
    static void True(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static Connection C(string id, long down, long up = 0, string target = "a.test", string node = "proxy") => new(id, target, "p.exe", node, down, up);
    static Snapshot S(params Connection[] c) => new(c.ToList(), 0, 0);
    static DateTime day = DateTime.Today.AddHours(12);
    static string Json(params (string Id, string[] Chains, string Host, long D, long U)[] items) => JsonSerializer.Serialize(new { connections = items.Select(i => new { id = i.Id, chains = i.Chains, metadata = new { host = i.Host, destinationIP = "1.2.3.4", process = "" }, download = i.D, upload = i.U }) });
    public static async Task Main(string[] args)
    {
        if (args.Length > 1 && args[0] == "--settings-worker") { ReliabilityTests.Worker(args[1]); return; }
        if(args.Contains("--timeline-benchmark")) { TimelineBenchmark.Run(); return; }
        var types = new Dictionary<string, string> { { "proxy", "Shadowsocks" }, { "GROUP", "Selector" }, { "customDirect", "Direct" }, { "block", "Reject" } };
        var parsed = ConnectionParser.Parse(Json(("p", new[] { "proxy", "GROUP" }, "a.test", 100, 5), ("d", new[] { "DIRECT", "GROUP" }, "direct.test", 9999, 9999), ("x", new[] { "customDirect", "GROUP" }, "direct2.test", 500, 500), ("u", new[] { "GROUP" }, "unknown.test", 100, 100), ("b", new[] { "block" }, "blocked.test", 0, 0), ("ip", new[] { "proxy" }, "", 100, 100)), types);
        Equal(2, parsed.Connections.Count, "proxy filter"); Equal(2, parsed.DirectCount, "resolved direct filter"); Equal(2, parsed.UnknownCount, "unknown and blocked routes excluded"); True(parsed.Connections.Last().Target == "1.2.3.4", "IP fallback"); True(parsed.Connections[0].Process == "未知", "missing process");
        var engine = new TrafficEngine(); Equal(0, engine.Sample(S(C("a", 999999)), 0, day).Count, "initial baseline"); Equal(0, engine.View(false).DownloadSpeed, "no historical speed spike");
        var changes = engine.Sample(S(C("a", 1000999)), 1, day.AddSeconds(1)); Equal(1000, changes.Sum(x => x.Download), "delta count"); Equal(1000, engine.View(false).DownloadSpeed, "actual one second speed");
        engine.Sample(S(C("a", 1002999)), 3, day.AddSeconds(3)); Equal(1000, engine.View(false).DownloadSpeed, "actual two second interval and average");
        engine.Sample(S(C("a", 1002999)), 4, day.AddSeconds(4)); Equal(2000.0 / 3, engine.View(false).DownloadSpeed, "three second window");
        engine.Sample(S(C("a", 1002999)), 7, day.AddSeconds(7)); Equal(0, engine.View(false).DownloadSpeed, "idle window drains"); True(engine.View(false).DownloadTop == null, "idle no winner");
        engine.Reset(); engine.Sample(S(C("a", 0), C("b", 0), C("c", 0, target: "b.test")), 0, day);
        engine.Sample(S(C("a", 100, 10), C("b", 200, 20), C("c", 50, 800, target: "b.test")), 1, day.AddSeconds(1));
        Equal(300, engine.View(false).DownloadTop!.DownloadSpeed, "domain aggregation"); True(engine.View(false).UploadTop!.Target == "b.test", "independent upload winner"); Equal(200, engine.View(true).DownloadTop!.DownloadSpeed, "single connection mode"); Equal(350, engine.View(false).DownloadSpeed, "aggregate proxy speed");
        engine.Sample(S(C("a", 101, 10)), 2, day.AddSeconds(2)); True(engine.View(false).DownloadTop!.Target == "a.test", "closed connections absent from winner");
        engine.Reset(); engine.Sample(S(C("a", 100, 100)), 0, day); Equal(0, engine.Sample(S(C("a", 50, 50)), 1, day.AddSeconds(1)).Count, "counter rollback baseline"); Equal(5, engine.Sample(S(C("a", 55, 55)), 2, day.AddSeconds(2)).Sum(x => x.Download), "rollback recovers");
        Equal(0, engine.Sample(S(C("a", 1000, 1000, node: "other")), 3, day.AddSeconds(3)).Count, "route changed baseline"); Equal(0, engine.View(false).DownloadSpeed, "route reset clears old rate");
        engine.Reset(); engine.Sample(S(C("a", 0)), 0, day); engine.Reset(); Equal(0, engine.Sample(S(C("a", 9000)), 100, day.AddSeconds(100)).Count, "reconnect ignores outage totals");
        engine.Reset(); var midnight = DateTime.Today.AddHours(23).AddMinutes(59).AddSeconds(59); engine.Sample(S(C("a", 0)), 0, midnight); var split = engine.Sample(S(C("a", 101, 3)), 2, midnight.AddSeconds(2)); Equal(2, split.Count, "midnight split"); Equal(101, split.Sum(x => x.Download), "midnight preserves bytes"); Equal(50, split.First().Download, "previous day proportion");
        engine.Reset(); engine.Sample(S(C("gap", 0)), 0, day); Equal(0, engine.Sample(S(C("gap", 1000000)), 60, day.AddSeconds(60)).Count, "long pause resets baseline");
        engine.Sample(S(C("gap", 1000100)), 61, day.AddSeconds(61)); Equal(100, engine.View(false).DownloadSpeed, "speed recovers after long pause");
        engine.Reset(); engine.Sample(S(C("a", 0)), 0, day); engine.Sample(S(C("a", 100), C("new", 999999, target: "new.test")), 1, day.AddSeconds(1)); Equal(100, engine.View(false).DownloadSpeed, "new connection excludes old bytes");
        var mixed = ConnectionParser.Parse("{\"connections\":[{\"id\":\"mixed\",\"chains\":[\"proxy\"],\"metadata\":{\"host\":\"EXAMPLE.COM.\",\"processPath\":\"C:\\\\Apps\\\\download.exe\"},\"download\":123,\"upload\":4}]}", types);
        True(mixed.Connections[0].Target == "example.com", "domain normalization"); True(mixed.Connections[0].Process == "download.exe", "process path fallback");
        engine.Reset();
        engine.Sample(S(
            new Connection("first", "same.test", "a.exe", "node-a", 0, 0),
            new Connection("second", "same.test", "b.exe", "node-b", 0, 0)), 0, day);
        engine.Sample(S(
            new Connection("first", "same.test", "a.exe", "node-a", 10, 20),
            new Connection("second", "same.test", "b.exe", "node-b", 30, 40)), 1, day.AddSeconds(1));
        True(engine.View(false).DownloadTop!.Process == "多个", "merged process label");
        True(engine.View(false).DownloadTop!.Node == "多个", "merged node label");
        True(engine.View(true).DownloadTop!.Process == "b.exe", "single connection metadata");
        engine.Reset();
        engine.Sample(S(C("z", 0, target: "z.test"), C("a", 0)), 0, day);
        engine.Sample(S(C("z", 100, target: "z.test"), C("a", 100)), 1, day.AddSeconds(1));
        True(engine.View(false).DownloadTop!.Target == "a.test", "stable equal-speed tie");
        True(engine.View(true).DownloadTop!.Key == "a", "stable single-connection tie");
        var path = Path.Combine(Path.GetTempPath(), "ProxyTrafficFloat-tests-" + Guid.NewGuid() + ".sqlite");
        try
        {
            using (var store = new TrafficStore(path))
            {
                store.Add(new[] { new Increment(day.Date, "a'中文.test", 100, 5), new Increment(day.Date, "a'中文.test", 200, 7), new Increment(day.Date.AddDays(-6), "b.test", 50, 9) });
                Equal(300, store.Query(day.Date, day.Date)[0].Download, "SQL quote and Unicode sum"); Equal(2, store.Query(day.Date.AddDays(-6), day.Date).Count, "seven day query");
                store.Prune(day.Date.AddDays(30)); Equal(0, store.Query(day.Date.AddYears(-1), day.Date.AddYears(1)).Count, "retention prune");
                store.Add(split); Equal(101, store.Query(midnight.Date, midnight.Date.AddDays(1)).Sum(r => r.Download), "midnight persistence");
            }
            using (var store = new TrafficStore(path)) { Equal(101, store.Query(midnight.Date, midnight.Date.AddDays(1)).Sum(r => r.Download), "restart persistence"); store.Clear(); Equal(0, store.Query(midnight.Date, midnight.Date.AddDays(1)).Count, "clear statistics"); }
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
        var secret = "local-test-secret"; var cipher = WindowsCrypto.Protect(secret); True(cipher != secret, "secret encrypted"); True(WindowsCrypto.Unprotect(cipher) == secret, "DPAPI roundtrip");
        using (MihomoClient.CreateClient("127.0.0.1:9090", "")) { }

        bool blocked = false; try { using var _ = MihomoClient.CreateClient("http://example.com", ""); } catch (ArgumentException) { blocked = true; }
        True(blocked, "remote endpoint blocked");
        var settingsPath = Path.Combine(Path.GetTempPath(), "ProxyTrafficFloat-settings-" + Guid.NewGuid() + ".json");
        try { var conf = new Settings { Visible = false, SingleConnection = true, Opacity = 0.6, Endpoint = "http://127.0.0.1:9090", Secret = secret }; conf.Save(settingsPath); var loaded = Settings.Load(settingsPath); True(!loaded.Visible && loaded.SingleConnection && loaded.Opacity == 0.6, "settings restart persistence"); True(loaded.Secret == secret, "saved encrypted secret decrypts"); True(!File.ReadAllText(settingsPath).Contains(secret), "secret not stored plaintext"); File.WriteAllText(settingsPath, "broken"); True(Settings.Load(settingsPath).ShowSpeed, "corrupt settings safe defaults"); } finally { if (File.Exists(settingsPath)) File.Delete(settingsPath); }
        await ReliabilityTests.Run();
        PresentationTests.Run();
        StorageTests.Run();
        TimelineTests.Run();
        LocalizationTests.Run();
        await TransportTests.Run();
        if (args.Contains("--live")) using (var client = new MihomoClient())
        {
            var live = await client.ReadAsync(new Settings()); True(live.DirectCount > 0, "live direct data"); True(live.Connections.Count > 0, "live proxy classification"); Console.WriteLine($"Live API: {client.Source}, proxy={live.Connections.Count}, direct={live.DirectCount}, unknown={live.UnknownCount}");
        }
        Console.WriteLine($"PASS: {assertions} assertions");
    }
}
