using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ProxyTrafficFloat;

public record Connection(string Id, string Target, string Process, string Node, long Download, long Upload);
public record Snapshot(List<Connection> Connections, int UnknownCount, int DirectCount);
public record Amount(long Download, long Upload);
public record Increment(DateTime Day, string Target, long Download, long Upload);
public record RankedTarget(string Key, string Target, string Process, string Node, double DownloadSpeed, double UploadSpeed);
public record TrafficView(double DownloadSpeed, double UploadSpeed, RankedTarget? DownloadTop, RankedTarget? UploadTop,
    int UnknownCount, int DirectCount, int ProxyCount);

public static class ConnectionParser
{
    static readonly HashSet<string> ProxyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ss", "ssr", "Shadowsocks", "ShadowsocksR", "Vmess", "Vless", "Trojan", "Socks5", "Http",
        "Hysteria", "Hysteria2", "Tuic", "WireGuard", "Snell", "Ssh", "AnyTLS", "Mieru"
    };

    public static Dictionary<string, string> ParseProxyTypes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        var proxies = Required(document.RootElement, "proxies", JsonValueKind.Object);
        foreach (var proxy in proxies.EnumerateObject())
            types[proxy.Name] = GetString(proxy.Value, "type");
        return types;
    }

    public static Snapshot Parse(string json, IReadOnlyDictionary<string, string> types)
    {
        using var document = JsonDocument.Parse(json);
        var connections = new List<Connection>();
        int unknown = 0, direct = 0;

        foreach (var item in Required(document.RootElement, "connections", JsonValueKind.Array).EnumerateArray())
        {
            // Mihomo reports the final outbound first, followed by its selector groups.
            string node = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("chains", out var chains) && chains.ValueKind == JsonValueKind.Array &&
                chains.GetArrayLength() > 0 && chains[0].ValueKind == JsonValueKind.String ? chains[0].GetString() ?? "" : "";
            types.TryGetValue(node, out var type);
            if (node.Equals("DIRECT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "Direct", StringComparison.OrdinalIgnoreCase))
            {
                direct++;
                continue;
            }
            if (type == null || !ProxyTypes.Contains(type))
            {
                unknown++;
                continue;
            }
            string id = GetString(item, "id");
            if (id.Length == 0 || !item.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
                !Counter(item, "download", out long download) || !Counter(item, "upload", out long upload))
            {
                unknown++;
                continue;
            }


            string host = GetString(metadata, "host");
            if (string.IsNullOrWhiteSpace(host)) host = GetString(metadata, "sniffHost");
            if (string.IsNullOrWhiteSpace(host)) host = GetString(metadata, "destinationIP");
            host = string.IsNullOrWhiteSpace(host) ? "未知目标" : host.Trim().TrimEnd('.').ToLowerInvariant();

            string process = GetString(metadata, "process");
            if (string.IsNullOrWhiteSpace(process)) process = Path.GetFileName(GetString(metadata, "processPath"));
            connections.Add(new(id, host, string.IsNullOrWhiteSpace(process) ? "未知" : process, node,
                download, upload));
        }
        return new(connections, unknown, direct);
    }

    public static string GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    static JsonElement Required(JsonElement root, string name, JsonValueKind kind) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == kind
            ? value : throw new JsonException("Invalid Mihomo response.");

    static bool Counter(JsonElement item, string name, out long number)
    {
        number = 0;
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out number) && number >= 0;
    }
}

public sealed class TrafficEngine
{
    record Frame(double Start, double End, Dictionary<string, (double Down, double Up)> Values);

    struct TargetRate
    {
        public string Target, Process, Node;
        public double Down, Up;

        public TargetRate(Connection connection)
        {
            Target = connection.Target;
            Process = connection.Process;
            Node = connection.Node;
            Down = Up = 0;
        }

        public void Add(Connection connection, double down, double up)
        {
            Down += down;
            Up += up;
            if (Process != connection.Process) Process = "多个";
            if (Node != connection.Node) Node = "多个";
        }

        public readonly RankedTarget Ranked(string key) => new(key, Target, Process, Node, Down, Up);
    }

    Dictionary<string, Connection> previous = new(StringComparer.Ordinal);
    readonly Queue<Frame> frames = new();
    double? previousTime;
    DateTime previousWall;
    Snapshot current = new(new(), 0, 0);

    public void Reset()
    {
        previous.Clear();
        frames.Clear();
        previousTime = null;
        current = new(new(), 0, 0);
    }

    public List<Increment> Sample(Snapshot snapshot, double time, DateTime wall)
    {
        var increments = new List<Increment>();
        if (previousTime is double last && time - last > 5) Reset();
        var now = new Dictionary<string, Connection>(snapshot.Connections.Count, StringComparer.Ordinal);
        foreach (var connection in snapshot.Connections) now.TryAdd(connection.Id, connection);

        if (previousTime is double before && time > before)
        {
            var values = new Dictionary<string, (double, double)>(now.Count, StringComparer.Ordinal);
            foreach (var connection in now.Values)
            {
                if (!previous.TryGetValue(connection.Id, out var old) || old.Node != connection.Node ||
                    old.Target != connection.Target || connection.Download < old.Download || connection.Upload < old.Upload)
                {
                    // New or restarted connections establish a baseline; never count historical bytes.
                    foreach (var oldFrame in frames) oldFrame.Values.Remove(connection.Id);
                    continue;
                }
                long down = connection.Download - old.Download, up = connection.Upload - old.Upload;
                if (down == 0 && up == 0) continue;
                values[connection.Id] = (down, up);
                AddIncrements(increments, connection.Target, down, up, previousWall, wall);
            }
            frames.Enqueue(new(before, time, values));
            while (frames.Count > 0 && frames.Peek().End <= time - 3) frames.Dequeue();
        }

        previous = now;
        previousTime = time;
        previousWall = wall;
        current = snapshot;
        return increments;
    }

    static void AddIncrements(List<Increment> increments, string target, long down, long up, DateTime start, DateTime end)
    {
        if (end <= start || start.Date == end.Date)
        {
            increments.Add(new(end.Date, target, down, up));
            return;
        }

        // Split a midnight interval proportionally; keep rounding remainder in the last day.
        long remainingDown = down, remainingUp = up;
        for (var cursor = start; cursor < end;)
        {
            var next = cursor.Date.AddDays(1);
            if (next > end) next = end;
            double fraction = (next - cursor).TotalSeconds / (end - start).TotalSeconds;
            long dayDown = next == end ? remainingDown : (long)(down * fraction);
            long dayUp = next == end ? remainingUp : (long)(up * fraction);
            if (dayDown != 0 || dayUp != 0) increments.Add(new(cursor.Date, target, dayDown, dayUp));
            remainingDown -= dayDown;
            remainingUp -= dayUp;
            cursor = next;
        }
    }

    public TrafficView View(bool single)
    {
        if (previousTime is not double time || frames.Count == 0)
            return new(0, 0, null, null, current.UnknownCount, current.DirectCount, current.Connections.Count);

        double start = Math.Max(time - 3, frames.Peek().Start), span = time - start;
        var rates = new Dictionary<string, (double Down, double Up)>(previous.Count, StringComparer.Ordinal);
        double totalDown = 0, totalUp = 0;
        foreach (var frame in frames)
        {
            double fraction = Math.Max(0, frame.End - Math.Max(start, frame.Start)) / (frame.End - frame.Start);
            foreach (var pair in frame.Values)
            {
                double down = pair.Value.Down * fraction / span, up = pair.Value.Up * fraction / span;
                totalDown += down;
                totalUp += up;
                rates.TryGetValue(pair.Key, out var old);
                rates[pair.Key] = (old.Down + down, old.Up + up);
            }
        }

        var groups = new Dictionary<string, TargetRate>(StringComparer.Ordinal);
        foreach (var connection in previous.Values)
        {
            string key = single ? connection.Id : connection.Target;
            if (!groups.TryGetValue(key, out var group)) group = new(connection);
            rates.TryGetValue(connection.Id, out var rate);
            group.Add(connection, rate.Down, rate.Up);
            groups[key] = group;
        }

        string? downKey = null, upKey = null;
        double fastestDown = 0, fastestUp = 0;
        foreach (var pair in groups)
        {
            if (Wins(pair.Value.Down, pair.Key, fastestDown, downKey))
            {
                fastestDown = pair.Value.Down;
                downKey = pair.Key;
            }
            if (Wins(pair.Value.Up, pair.Key, fastestUp, upKey))
            {
                fastestUp = pair.Value.Up;
                upKey = pair.Key;
            }
        }
        var downTop = downKey == null ? null : groups[downKey].Ranked(downKey);
        var upTop = upKey == null ? null : upKey == downKey ? downTop : groups[upKey].Ranked(upKey);
        return new(totalDown, totalUp, downTop, upTop, current.UnknownCount, current.DirectCount, current.Connections.Count);
    }

    static bool Wins(double speed, string key, double best, string? bestKey) =>
        speed > 0 && (speed > best || speed == best && string.Compare(key, bestKey, StringComparison.Ordinal) < 0);
}

public static class Display
{
    static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Bytes(double amount)
    {
        int unit = 0;
        while (amount >= 1024 && unit < Units.Length - 1)
        {
            amount /= 1024;
            unit++;
        }
        return $"{amount:0.##} {Units[unit]}";
    }

    public static string Speed(double amount) => Bytes(amount) + "/s";
}
