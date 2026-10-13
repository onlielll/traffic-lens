using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProxyTrafficFloat;

public sealed record NodeLatency(string Node, int Delay, DateTimeOffset At, int[] Samples, bool Slow)
{
    [JsonIgnore]
    public bool Fresh => DateTimeOffset.UtcNow - At < TimeSpan.FromMinutes(10);
    [JsonIgnore]
    public string Caption => Node + " · " + (Delay > 0 ? Delay + " ms" : Strings.T("测速失败")) +
        (Fresh ? Slow ? " · " + Strings.T("节点较慢") : "" : " · " + Strings.T("旧测速结果"));

    public static NodeLatency? Parse(string json, string node, double threshold)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array || history.GetArrayLength() == 0) return null;
        int count = Math.Min(60, history.GetArrayLength());
        var values = new int[count];
        DateTimeOffset at = default;
        for (int i = 0; i < count; i++)
        {
            var point = history[history.GetArrayLength() - count + i];
            if (point.ValueKind != JsonValueKind.Object) continue;
            if (point.TryGetProperty("delay", out var delay) && delay.ValueKind == JsonValueKind.Number && delay.TryGetInt32(out int value)) values[i] = Math.Max(0, value);
            if (point.TryGetProperty("time", out var time) && time.ValueKind == JsonValueKind.String) DateTimeOffset.TryParse(time.GetString(), out at);
        }
        return new(node, values[^1], at, values, count >= 2 && values[^1] >= threshold && values[^2] >= threshold);
    }
}