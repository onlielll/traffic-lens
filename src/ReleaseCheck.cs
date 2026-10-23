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

    public static readonly Uri ReleasesPage = new("https://github.com/onlielll/traffic-lens/releases");

    public static async Task<ReleaseInfo?> FetchAsync(bool previews, CancellationToken cancellation)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TrafficLens-UpdateCheck");
        return await FetchAsync(client, previews, cancellation);
    }

    internal static async Task<ReleaseInfo?> FetchAsync(HttpClient client, bool previews, CancellationToken cancellation)
    {
        try
        {
            return Parse(await client.GetStringAsync("https://raw.githubusercontent.com/onlielll/traffic-lens/main/updates.json", cancellation), previews);
        }
        catch (Exception error) when (!cancellation.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or JsonException)
        {
            // The small release feed avoids shared-IP API quotas; the API remains a single fallback.
            return Parse(await client.GetStringAsync("https://api.github.com/repos/onlielll/traffic-lens/releases?per_page=20", cancellation), previews);
        }
    }

    public static string FailureMessage(Exception error) => error switch
    {
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests } => "GitHub 请求受限，请稍后重试或打开版本页。",
        OperationCanceledException => "连接超时，请检查网络或代理后重试。",
        HttpRequestException => "无法连接更新服务，请检查网络或代理。",
        _ => "版本信息暂时不可用，请打开版本页。"
    };
}
