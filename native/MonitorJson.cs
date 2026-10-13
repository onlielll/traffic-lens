using System;
using System.Text.Json.Serialization;

namespace ProxyTrafficFloat.Native;

public sealed record LiveStatus(bool connected, string status, string source, DateTimeOffset at, NodeLatency? latency = null);
public sealed record ExitStatus(DateTimeOffset at, string reason, int success, int failed);
#if TRAFFIC_LENS_VERIFICATION
public sealed record SoakStart(string kind, DateTimeOffset started, int durationSeconds, string renderer);
public sealed record SoakSample(string kind, double seconds, int success, int failed,
    long workingSet, long privateBytes, long managed, int handles, uint gdi, uint user);
public sealed record SoakResult(string kind, double seconds, int success, int failed, bool? resourcesStable, long privateGrowth);

#endif

// Compile serializers once. Sampling does not build reflection metadata at runtime.
[JsonSerializable(typeof(LiveStatus))]
[JsonSerializable(typeof(ExitStatus))]
#if TRAFFIC_LENS_VERIFICATION
[JsonSerializable(typeof(SoakStart))]
[JsonSerializable(typeof(SoakSample))]
[JsonSerializable(typeof(SoakResult))]
#endif
internal partial class MonitorJson : JsonSerializerContext
{
}
