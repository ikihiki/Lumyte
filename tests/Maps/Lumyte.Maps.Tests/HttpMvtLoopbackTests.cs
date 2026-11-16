using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Maps.Http;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Tests;

/// <summary>Real native sockets, framing, compression and cancellation, using only loopback HTTP.</summary>
public sealed class HttpMvtLoopbackTests
{
    /// <summary>Native source handles conditional 304, 404, 401 and selected 429/5xx retries.</summary>
    /// <param name="status">Initial server status.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData(200)]
    [InlineData(304)]
    [InlineData(404)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task NativeStatusAndRetryAsync(int status)
    {
        byte[] payload = [1, 2, 3];
        await using var server = new Loopback((count, _) => Task.FromResult(count == 1 && status != 200
            ? Response(status, [], status is 429 or 500 or 502 or 503 or 504 ? "Retry-After: 0" : null)
            : Response(200, payload, "ETag: \"one\"")));
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration);
        ResourceId id = registration.GetKey(new(0, 0, 0)).Address.Id;
        switch (status)
        {
            case 304:
                await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(id));
                break;
            case 404:
                Assert.Null(await source.GetRevisionAsync(id));
                Assert.Null(await source.GetRevisionAsync(id));
                Assert.Equal(1, server.Count);
                break;
            case 401:
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.GetRevisionAsync(id));
                Assert.Equal(1, server.Count);
                break;
            default:
                Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), await source.GetRevisionAsync(id));
                Assert.Equal(status == 200 ? 1 : 2, server.Count);
                break;
        }
    }

    /// <summary>Conditional requests carry the validator and a 304 retains the exact accepted content hash.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task NativeConditional304Async()
    {
        await using var server = new Loopback((count, request) =>
        {
            if (count == 1)
            {
                return Task.FromResult(Response(200, [1], "ETag: \"one\""));
            }

            Assert.Contains("If-None-Match: \"one\"", request, StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(Response(304, [], "ETag: \"one\""));
        });
        var clock = new OffsetClock();
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration, timeProvider: clock);
        ResourceId id = registration.GetKey(new(0, 0, 0)).Address.Id;
        string? revision = await source.GetRevisionAsync(id);
        clock.Offset = TimeSpan.FromSeconds(31);
        Assert.Equal(revision, await source.GetRevisionAsync(id));
        Assert.Equal(2, server.Count);
    }

    /// <summary>Real HTTP framing rejects truncation and the counted source rejects unknown-length overflows.</summary>
    /// <param name="mode">Invalid framing or capacity mode.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData("truncated")]
    [InlineData("unknown")]
    [InlineData("wire")]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task NativeFramingAndCapacityAsync(string mode)
    {
        byte[] body = new byte[1025];
        string? extra = null;
        if (mode is "gzip" or "br")
        {
            using var output = new MemoryStream();
            using (Stream compressor = mode == "gzip" ? new GZipStream(output, CompressionLevel.SmallestSize, true) : new BrotliStream(output, CompressionLevel.SmallestSize, true))
            {
                compressor.Write(body);
            }

            body = output.ToArray();
            extra = $"Content-Encoding: {mode}";
        }

        byte[] response = mode == "truncated" ? Response(200, [1], length: 99) : Response(200, body, extra, length: mode is "unknown" or "wire" ? -1 : null);
        await using var server = new Loopback((_, _) => Task.FromResult(response));
        HttpMvtRegistration registration = Registration(server.Scope, maxWire: mode == "wire" ? 16 : 4096, maxExpanded: 1024);
        await using var source = new HttpMvtSource(registration);
        if (mode == "truncated")
        {
            await Assert.ThrowsAnyAsync<IOException>(async () => await source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id));
        }
    }

    /// <summary>Actual native response reads terminate on source stop even after headers have arrived.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task NativeStoppingCancelsBodyReadAsync()
    {
        await using var server = new Loopback((_, _) => Task.FromResult(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: 100\r\nConnection: close\r\n\r\n")), holdOpen: true);
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration);
        Task<string?> read = source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id).AsTask();
        await server.RequestArrived.Task;
        await source.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
    }

    /// <summary>Manual native redirect follows a scoped relative path but never cross-origin.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task NativeRedirectRejectsDifferentPortAsync()
    {
        await using var server = new Loopback((_, _) => Task.FromResult(Response(302, [], "Location: http://127.0.0.1:1/tiles/no.pbf")));
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration, new("Bearer", "local-fixture"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id));
        Assert.Equal(1, server.Count);
    }

    /// <summary>A total deadline cancels a native response using injected deterministic timers.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task NativeDeadlineUsesInjectedTimerAsync()
    {
        await using var server = new Loopback((_, _) => Task.FromResult(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: 100\r\nConnection: close\r\n\r\n")), holdOpen: true);
        var clock = new HttpMvtTests.ManualTime();
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration, timeProvider: clock);
        Task<string?> pending = source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id).AsTask();
        await server.RequestArrived.Task;
        clock.Advance(TimeSpan.FromSeconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    private static HttpMvtRegistration Registration(Uri scope, int maxWire = 4096, int maxExpanded = 4096) => new("native-local", "1", new("native"), new(scope, "fixture", maxWireBytes: maxWire, maxExpandedBytes: maxExpanded), new Dictionary<MapTileId, Uri> { [new(0, 0, 0)] = new(scope, "0.pbf") });

    private static byte[] Response(int status, byte[] body, string? extra = null, int? length = null)
    {
        string headers = $"HTTP/1.1 {status} fixture\r\nContent-Type: application/vnd.mapbox-vector-tile\r\nConnection: close\r\n";
        if (length != -1)
        {
            headers += $"Content-Length: {length ?? body.Length}\r\n";
        }

        if (extra is not null)
        {
            headers += extra + "\r\n";
        }

        return [.. Encoding.ASCII.GetBytes(headers + "\r\n"), .. body];
    }

    private sealed class OffsetClock : TimeProvider
    {
        internal TimeSpan Offset { get; set; }

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed class Loopback : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private int _count;

        internal Loopback(Func<int, string, Task<byte[]>> respond, bool holdOpen = false)
        {
            _listener.Start();
            Scope = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tiles/");
            _run = RunAsync(respond, holdOpen);
        }

        internal Uri Scope { get; }

        internal int Count => Volatile.Read(ref _count);

        internal TaskCompletionSource RequestArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try
            {
                await _run;
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }

        private async Task RunAsync(Func<int, string, Task<byte[]>> respond, bool holdOpen)
        {
            while (!_stop.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using NetworkStream stream = client.GetStream();
                var request = new StringBuilder();
                byte[] one = new byte[1];
                while (request.Length < 16384 && await stream.ReadAsync(one, _stop.Token) != 0)
                {
                    request.Append((char)one[0]);
                    if (request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        break;
                    }
                }

                byte[] response = await respond(Interlocked.Increment(ref _count), request.ToString());
                await stream.WriteAsync(response, _stop.Token);
                RequestArrived.TrySetResult();
                if (holdOpen)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, _stop.Token);
                }
            }
        }
    }
}
