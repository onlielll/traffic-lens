using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ProxyTrafficFloat;

public sealed class MihomoClient : IDisposable
{
    HttpClient? client;
    Dictionary<string, string> types = new(StringComparer.Ordinal);
    DateTime typesAt;

    public string Source { get; private set; } = "未连接";

    public async Task<Snapshot> ReadAsync(Settings settings, CancellationToken cancellation = default)
    {
        if (client == null) await ConnectAsync(settings, cancellation);
        try
        {
            if (types.Count == 0 || DateTime.UtcNow - typesAt > TimeSpan.FromMinutes(1))
            {
                types = ConnectionParser.ParseProxyTypes(await client!.GetStringAsync("proxies", cancellation));
                typesAt = DateTime.UtcNow;
            }
            return ConnectionParser.Parse(await client!.GetStringAsync("connections", cancellation), types);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    async Task ConnectAsync(Settings settings, CancellationToken cancellation)
    {
        var candidates = new List<(string Address, string Secret)>();
        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            candidates.Add((settings.Endpoint.Trim(), settings.Secret));
        }
        else
        {
            // Discover the running Verge pipe first; configuration can contain a stale pipe name.
            try
            {
                var names = Directory.GetFiles(@"\\.\pipe\").Select(Path.GetFileName).ToArray();
                candidates.AddRange(names.Where(name => name!.StartsWith("verge-mihomo-production-", StringComparison.Ordinal))
                    .Take(3).Select(name => (@"\\.\pipe\" + name, "")));
                candidates.AddRange(names.Where(name => name!.StartsWith("verge-mihomo-sidecar-", StringComparison.Ordinal))
                    .Take(2).Select(name => (@"\\.\pipe\" + name, "")));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "io.github.clash-verge-rev.clash-verge-rev");
            foreach (string filename in new[] { "clash-verge.yaml", "config.yaml" })
            {
                string file = Path.Combine(root, filename);
                if (!File.Exists(file)) continue;
                var lines = File.ReadAllLines(file);
                string Value(string key) => lines.FirstOrDefault(line => line.StartsWith(key + ":", StringComparison.Ordinal))
                    ?.Split(':', 2)[1].Trim().Trim('\'', '"') ?? "";
                string pipe = Value("external-controller-pipe");
                if (pipe.Length > 0) candidates.Add((pipe, ""));
                string address = Value("external-controller");
                if (address.Length > 0) candidates.Add((address, Value("secret")));
            }
        }

        foreach (var candidate in candidates.Distinct().Take(8))
        {
            HttpClient? attempt = null;
            try
            {
                attempt = CreateClient(candidate.Address, candidate.Secret);
                types = ConnectionParser.ParseProxyTypes(await attempt.GetStringAsync("proxies", cancellation));
                typesAt = DateTime.UtcNow;
                client = attempt;
                Source = candidate.Address.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
                    ? "命名管道" : attempt.BaseAddress!.Authority;
                return;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                attempt?.Dispose();
                throw;
            }
            catch { attempt?.Dispose(); }
        }
        throw new IOException("无法连接 Mihomo，请确认 Clash 正在运行，或在设置中填写本地接口。");
    }

    public static HttpClient CreateClient(string endpoint, string secret)
    {
        Uri address;
        string? pipeName = null;
        if (endpoint.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
        {
            pipeName = endpoint[9..];
            if (pipeName.Length == 0 || pipeName.Contains('\\') || pipeName.Contains('/'))
                throw new ArgumentException("命名管道格式无效");
            address = new("http://localhost/");
        }
        else
        {
            if (!endpoint.Contains("://")) endpoint = "http://" + endpoint;
            if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out address!) ||
                !address.IsLoopback || address.Scheme != "http" || address.UserInfo.Length > 0 ||
                address.Query.Length > 0 || address.Fragment.Length > 0)
                throw new ArgumentException("仅支持本机 HTTP 地址，例如 http://127.0.0.1:9090");
        }

        // Never route monitoring through the system proxy or follow redirects outside the local API.
        var handler = new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false };
        if (pipeName != null)
        {
            handler.ConnectCallback = async (_, token) =>
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(token);
                    return pipe;
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }
            };
        }
        var result = new HttpClient(handler) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(2) };
        if (!string.IsNullOrEmpty(secret)) result.DefaultRequestHeaders.Authorization = new("Bearer", secret);
        return result;
    }

    public void Dispose()
    {
        client?.Dispose();
        client = null;
        types.Clear();
    }
}
