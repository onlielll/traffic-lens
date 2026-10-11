using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ProxyTrafficFloat;

public static class TransportTests
{
    const string Proxies = "{\"proxies\":{\"mock\":{\"type\":\"Vless\"}}}";
    const string Connections = "{\"connections\":[{\"id\":\"one\",\"chains\":[\"mock\"],\"metadata\":{\"host\":\"mock.test\"},\"download\":123,\"upload\":4}]}";
    public static async Task Run()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        int connectionRequests = 0, proxyRequests = 0; bool authenticated = true;
        var serving = Task.Run(async () =>
        {
            try
            {
                while (!timeout.IsCancellationRequested)
                {
                    using var socket = await listener.AcceptTcpClientAsync(timeout.Token); using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true);
                    var line = await reader.ReadLineAsync(timeout.Token) ?? ""; string header; bool auth = false;
                    while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(timeout.Token) ?? "")) if (header == "Authorization: Bearer test-token") auth = true;
                    authenticated &= auth;
                    bool proxies = line.Contains("/proxies"); if (proxies) proxyRequests++; else connectionRequests++;
                    bool fail = !proxies && connectionRequests == 2;
                    var payload = proxies ? Proxies : Connections;
                    // Chunked HTTP exercises the same decoding used by the actual Windows pipe API.
                    var bytes = Encoding.UTF8.GetBytes(payload); var response = $"HTTP/1.1 {(fail ? "500 Error" : "200 OK")}\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n{bytes.Length:x}\r\n{payload}\r\n0\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), timeout.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        });
        try
        {
            using var client = new MihomoClient(); var settings = new Settings { Endpoint = $"http://127.0.0.1:{port}", Secret = "test-token" };
            if ((await client.ReadAsync(settings, timeout.Token)).Connections.Count != 1) throw new Exception("mock HTTP connection missing");
            bool failed = false; try { await client.ReadAsync(settings, timeout.Token); } catch (System.Net.Http.HttpRequestException) { failed = true; }
            if (!failed) throw new Exception("HTTP error not surfaced");
            if ((await client.ReadAsync(settings, timeout.Token)).Connections.Count != 1 || proxyRequests != 2 || !authenticated) throw new Exception("HTTP reconnect / authentication failed");
        }
        finally { timeout.Cancel(); listener.Stop(); await serving; }
        var pipeName = "ProxyTrafficFloat-test-" + Guid.NewGuid(); using var pipeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pipeServer = Task.Run(async () =>
        {
            for (int i = 0; i < 2; i++)
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(pipeTimeout.Token); using var reader = new StreamReader(server, Encoding.ASCII, false, 4096, true);
                var line = await reader.ReadLineAsync(pipeTimeout.Token) ?? ""; while (!string.IsNullOrEmpty(await reader.ReadLineAsync(pipeTimeout.Token))) { }
                var payload = line.Contains("/proxies") ? Proxies : Connections; var bytes = Encoding.UTF8.GetBytes(payload);
                await server.WriteAsync(Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n{payload}"), pipeTimeout.Token); await server.FlushAsync(pipeTimeout.Token);
                server.WaitForPipeDrain();
            }
        });
        using (var client = new MihomoClient()) { if ((await client.ReadAsync(new Settings { Endpoint = @"\\.\pipe\" + pipeName }, pipeTimeout.Token)).Connections.Count != 1) throw new Exception("named pipe manual interface failed"); }
        await pipeServer;
        using (var client = new MihomoClient())
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)); bool cancelled = false;
            try { await client.ReadAsync(new Settings { Endpoint = @"\\.\pipe\not-present-" + Guid.NewGuid() }, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled) throw new Exception("pipe cancellation failed");
        }
        Console.WriteLine("PASS: mock HTTP authentication, chunk decoding, failure/reconnect, manual named pipe, cancellation");
    }

}
