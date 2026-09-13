using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Lumyte.Graphics.WebGPU.Browser.Tests;

internal sealed class BrowserStaticServer : IAsyncDisposable
{
    private readonly string root;
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentBag<Task> connections = [];
    private readonly Task accepting;

    public BrowserStaticServer(string root)
    {
        this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        listener.Start();
        BaseAddress = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
        accepting = AcceptAsync();
    }

    public Uri BaseAddress { get; }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stopping.Token);
                connections.Add(ServeAsync(client));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (SocketException) when (stopping.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                string? request = await reader.ReadLineAsync(stopping.Token);
                if (request is null) { return; }
                int headerLength = request.Length;
                while (await reader.ReadLineAsync(stopping.Token) is { Length: > 0 } header)
                {
                    headerLength += header.Length;
                    if (headerLength > 32768) { return; }
                }

                string[] fields = request.Split(' ', 3);
                if (fields.Length < 2 || fields[0] is not ("GET" or "HEAD"))
                {
                    await RespondAsync(stream, "405 Method Not Allowed", "text/plain", []);
                    return;
                }
                Uri uri = new(BaseAddress, fields[1]);
                string relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
                string file = Path.GetFullPath(Path.Combine(root, relative.Length == 0 ? "index.html" : relative));
                if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                {
                    await RespondAsync(stream, "404 Not Found", "text/plain", []);
                    return;
                }
                string mime = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".js" or ".mjs" => "text/javascript; charset=utf-8",
                    ".json" => "application/json",
                    ".wasm" => "application/wasm",
                    _ => "application/octet-stream",
                };
                byte[] contents = await File.ReadAllBytesAsync(file, stopping.Token);
                await RespondAsync(stream, "200 OK", mime, contents, fields[0] == "HEAD");
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            catch (IOException) { /* A browser can cancel an outstanding asset request while shutting down. */ }
        }
    }

    private async Task RespondAsync(NetworkStream stream, string status, string mime, byte[] contents, bool head = false)
    {
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {mime}\r\nContent-Length: {contents.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, stopping.Token);
        if (!head) { await stream.WriteAsync(contents, stopping.Token); }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        await accepting;
        await Task.WhenAll(connections);
        stopping.Dispose();
    }
}
