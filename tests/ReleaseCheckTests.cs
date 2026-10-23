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
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Handler(Func<int, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public readonly List<Uri> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            return reply(Requests.Count, token);
        }
    }
    static HttpResponseMessage Response(HttpStatusCode code, string body = Releases) => new(code) { Content = new StringContent(body) };
    public static async Task Run()
    {
        using (var handler = new Handler((n, _) => Task.FromResult(Response(HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
        {
            var result = await ReleaseCheck.FetchAsync(client, true, default);
            Check(result?.Tag == "v2.1.0" && handler.Requests.Count == 1, "Static feed must work without anonymous API quota");
            Check(handler.Requests[0].Host == "raw.githubusercontent.com", "Use only the first-party feed");
            Check(await ReleaseCheck.FetchAsync(client, false, default) == null, "Preview-only feed must respect opt-out without calling API");
            Check(handler.Requests.Count == 2, "No extra API request for a valid preview-only feed");
        }
        using (var handler = new Handler((n, _) => Task.FromResult(Response(n == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
        {
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0", "API fallback after unavailable static feed");
            Check(handler.Requests.Count == 2 && handler.Requests[1].Host == "api.github.com", "Fallback must be bounded and first-party");
        }
        using (var handler = new Handler((n, _) => Task.FromResult(Response(HttpStatusCode.OK, n == 1 ? "not-json" : Releases))))
        using (var client = new HttpClient(handler))
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0", "Malformed feed falls back once");
        using (var handler = new Handler((n, _) => n == 1 ? Task.FromException<HttpResponseMessage>(new TaskCanceledException()) : Task.FromResult(Response(HttpStatusCode.OK))))
        using (var client = new HttpClient(handler))
            Check((await ReleaseCheck.FetchAsync(client, true, default))?.Tag == "v2.1.0", "Request timeout permits bounded fallback");
        using (var stop = new CancellationTokenSource())
        using (var handler = new Handler((_, token) => { stop.Cancel(); return Task.FromCanceled<HttpResponseMessage>(token); }))
        using (var client = new HttpClient(handler))
        {
            bool cancelled = false;
            try { await ReleaseCheck.FetchAsync(client, true, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && handler.Requests.Count == 1, "Window-close cancellation must not make another request");
        }
        using (var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.Forbidden))))
        using (var client = new HttpClient(handler))
        {
            bool limited = false;
            try { await ReleaseCheck.FetchAsync(client, true, default); }
            catch (HttpRequestException error) { limited = error.StatusCode == HttpStatusCode.Forbidden; }
            Check(limited && handler.Requests.Count == 2, "Final rate limit surfaced without retry loop");
        }
        Check(ReleaseCheck.FailureMessage(new HttpRequestException("", null, HttpStatusCode.Forbidden)).Contains("受限"), "Explain rate limit");
        Check(ReleaseCheck.FailureMessage(new TaskCanceledException()).Contains("超时"), "Explain timeout");
        Check(ReleaseCheck.FailureMessage(new HttpRequestException()).Contains("网络"), "Explain connection failure");
        Check(ReleaseCheck.FailureMessage(new JsonException()).Contains("版本信息"), "Explain bad metadata");
        Console.WriteLine("PASS: first-party update feed, preview-only filter, single API fallback, timeout/cancellation and actionable errors");
    }
}
