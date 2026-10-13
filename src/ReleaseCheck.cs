using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProxyTrafficFloat;

public sealed record ReleaseInfo(string Tag, Version Version, Uri Page);

public static class ReleaseCheck
{
    public static ReleaseInfo? Parse(string json, bool previews)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid release list.");
        ReleaseInfo? newest = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("draft", out var draft) || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !item.TryGetProperty("prerelease", out var preview) || preview.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || draft.GetBoolean() || (!previews && preview.GetBoolean())) continue;
            string tag = ConnectionParser.GetString(item, "tag_name");
            if (!Version.TryParse(tag.TrimStart('v').Split('-')[0], out var version)) continue;
            if (!Uri.TryCreate(ConnectionParser.GetString(item, "html_url"), UriKind.Absolute, out var page) || page.Scheme != "https" || page.Host != "github.com" || !page.AbsolutePath.StartsWith("/onlielll/traffic-lens/releases/", StringComparison.Ordinal)) continue;
            if (newest == null || version > newest.Version) newest = new(tag, version, page);
        }
        return newest;
    }

    public static async Task<ReleaseInfo?> FetchAsync(bool previews, CancellationToken cancellation)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TrafficLens-UpdateCheck");
        return Parse(await client.GetStringAsync("https://api.github.com/repos/onlielll/traffic-lens/releases?per_page=20", cancellation), previews);
    }
}