using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ProxyTrafficFloat;

static class ReleaseCheckTests
{
    const string Releases = "[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v2.1.0\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.1.0\"}]";
    const string NewReleases = "[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v2.2.0\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.2.0\"}]";
    const string StableReleases = "[{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.1.0\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.1.0\"}]";
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Handler(Func<Uri, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public readonly List<Uri> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            return reply(request.RequestUri!, token);
        }
    }
    static HttpResponseMessage Response(HttpStatusCode code, string body = Releases) => new(code) { Content = new StringContent(body) };
    public static async Task Run()
    {
        using (var handler = new Handler((uri, _) => Task.FromResult(Response(HttpStatusCode.OK, uri.Host == "api.github.com" ? NewReleases : Releases))))
        using (var client = new HttpClient(handler))
        {
            var result = await ReleaseCheck.FetchAsync(client, true, default);
            Check(result?.Tag == "v2.2.0", "A valid but stale static feed must not hide a newly published release");
            Check(handler.Requests.Count == 1 && handler.Requests[0].Host == "api.github.com", "Live release metadata succeeds without requesting the stale feed");
        }
        using (var handler = new Handler((uri, _) => Task.FromResult(Response(HttpStatusCode.OK, uri.Host == "api.github.com" ? StableReleases : Releases))))
        using (var client = new HttpClient(handler))
            Check((await ReleaseCheck.FetchAsync(client, false, default))?.Tag == "v2.1.0", "A release changed from preview to stable must be visible despite stale preview metadata");

        string equalVersions = "[{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v2.2.0-preview\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.2.0-preview\"},{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.2.0\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.2.0\"}]";
        Check(ReleaseCheck.Parse(equalVersions, true)?.Tag == "v2.2.0", "Stable release wins when a preview with the same numeric version appears first");
        string stableFirst = "[{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v2.2.0\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.2.0\"},{\"draft\":false,\"prerelease\":true,\"tag_name\":\"v2.2.0-preview\",\"html_url\":\"https://github.com/onlielll/traffic-lens/releases/tag/v2.2.0-preview\"}]";
        Check(ReleaseCheck.Parse(stableFirst, true)?.Tag == "v2.2.0", "Equal-version preview must not replace a stable release");

        using (var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
        {
            Check(await ReleaseCheck.FetchAsync(client, false, default) == null, "Preview-only live releases respect opt-out without consulting stale fallback metadata");
            Check(handler.Requests.Count == 1 && handler.Requests[0].Host == "api.github.com", "A valid preview-only API response needs no fallback");
        }
        using (var handler = new Handler((uri, _) => Task.FromResult(Response(uri.Host == "api.github.com" ? HttpStatusCode.Forbidden : HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
        {
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0", "Static feed remains usable when the API quota is exhausted");
            Check(handler.Requests.Count == 2 && handler.Requests[1].Host == "raw.githubusercontent.com", "Rate-limit fallback is bounded and first-party");
        }
        using (var handler = new Handler((uri, _) => uri.Host == "api.github.com" ? Task.FromException<HttpResponseMessage>(new HttpRequestException()) : Task.FromResult(Response(HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0" && handler.Requests.Count == 2, "Network failure falls back once to the static feed");
        using (var handler = new Handler((uri, _) => Task.FromResult(Response(HttpStatusCode.OK, uri.Host == "api.github.com" ? "not-json" : Releases))))
        using (var client = new HttpClient(handler))
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0" && handler.Requests.Count == 2, "Malformed API metadata falls back once");
        using (var handler = new Handler((uri, _) => uri.Host == "api.github.com" ? Task.FromException<HttpResponseMessage>(new TaskCanceledException()) : Task.FromResult(Response(HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0" && handler.Requests.Count == 2, "API timeout permits one static fallback");
        using (var stop = new CancellationTokenSource())
        using (var handler = new Handler((_, token) => { stop.Cancel(); return Task.FromCanceled<HttpResponseMessage>(token); }))
        using (var client = new HttpClient(handler))
        {
            bool cancelled = false;
            try { await ReleaseCheck.FetchAsync(client, true, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && handler.Requests.Count == 1, "Window-close cancellation must not make a fallback request");
        }
        using (var stop = new CancellationTokenSource())
        using (var handler = new Handler((uri, token) =>
        {
            if (uri.Host == "api.github.com") return Task.FromResult(Response(HttpStatusCode.Forbidden));
            stop.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        }))
        using (var client = new HttpClient(handler))
        {
            bool cancelled = false;
            try { await ReleaseCheck.FetchAsync(client, true, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && handler.Requests.Count == 2, "Cancellation during fallback terminates without retrying");
        }
        using (var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.Forbidden))))
        using (var client = new HttpClient(handler))
        {
            bool limited = false;
            try { await ReleaseCheck.FetchAsync(client, true, default); }
            catch (HttpRequestException error) { limited = error.StatusCode == HttpStatusCode.Forbidden; }
            Check(limited && handler.Requests.Count == 2, "Final failure is surfaced without a retry loop");
        }
        Check(ReleaseCheck.FailureMessage(new HttpRequestException("", null, HttpStatusCode.Forbidden)).Contains("受限"), "Explain rate limit");
        Check(ReleaseCheck.FailureMessage(new TaskCanceledException()).Contains("超时"), "Explain timeout");
        Check(ReleaseCheck.FailureMessage(new HttpRequestException()).Contains("网络"), "Explain connection failure");
        Check(ReleaseCheck.FailureMessage(new JsonException()).Contains("版本信息"), "Explain bad metadata");
        Console.WriteLine("PASS: live update metadata over stale feed, stable tie preference, preview filter, single static fallback, rate limits, timeout/cancellation and actionable errors");
    }
}
