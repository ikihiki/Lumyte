using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Lumyte.Maps.Http;
using Lumyte.Resources;
using Xunit;
using Xunit.Abstractions;

namespace Lumyte.Maps.Tests;

/// <summary>Real native sockets, framing, compression and cancellation, using only loopback HTTP.</summary>
/// <param name="output">Functional fixture counters.</param>
public sealed class HttpMvtLoopbackTests(ITestOutputHelper output)
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

    /// <summary>Actual native HTTP accepts case-insensitive representation tokens.</summary>
    /// <param name="coding">Mixed-case coding.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData("GZip")]
    [InlineData("BR")]
    public async Task NativeMixedCaseTokensAsync(string coding)
    {
        byte[] payload = [1, 2, 3];
        using var output = new MemoryStream();
        using (Stream encoder = coding == "GZip" ? new GZipStream(output, CompressionLevel.SmallestSize, true) : new BrotliStream(output, CompressionLevel.SmallestSize, true))
        {
            encoder.Write(payload);
        }

        byte[] response = Response(200, output.ToArray(), $"Content-Encoding: {coding}");
        string header = Encoding.Latin1.GetString(response).Replace("application/vnd.mapbox-vector-tile", "Application/Vnd.Mapbox-Vector-Tile", StringComparison.Ordinal);
        await using var server = new Loopback((_, _) => Task.FromResult(Encoding.Latin1.GetBytes(header)));
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), await source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id));
        Assert.Equal(1, server.Count);
    }

    /// <summary>Unread keep-alive bodies must close within the operation cleanup budget, not drain after terminal admission.</summary>
    /// <param name="status">Redirect, retry or content rejection.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData(302)]
    [InlineData(503)]
    [InlineData(200)]
    public async Task NativeUnreadKeepAliveDoesNotDrainAsync(int status)
    {
        await using var server = new UnreadKeepAlive(status);
        HttpMvtRegistration registration = Registration(server.Scope);
        await using var source = new HttpMvtSource(registration);
        Task<string?> operation = source.GetRevisionAsync(registration.GetKey(new(0, 0, 0)).Address.Id).AsTask();
        if (status == 200)
        {
            await Assert.ThrowsAsync<InvalidDataException>(async () => await operation);
        }
        else
        {
            Assert.NotNull(await operation);
        }

        // The body is withheld behind a barrier. No wall-clock throughput assertion:
        // one second is the fixture's functional terminal socket-cleanup deadline.
        await server.FirstClosed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(status == 200 ? 1 : 2, server.Accepted);
        Assert.Equal(1, server.ClosedUnread);
        await source.DisposeAsync();
        server.ReleaseBody.TrySetResult();
        await server.DelayedBodyFinished.Task;
        Assert.Equal(0, server.DelayedBodyWrites);
        Assert.Equal(0, server.UnreadConnections);
        output.WriteLine($"keep-alive status={status}: accepted={server.Accepted}; closed-unread={server.ClosedUnread}; delayed-body-writes={server.DelayedBodyWrites}; unread-after-stop={server.UnreadConnections}");
    }

    /// <summary>Accept shutdown errors are ignored only after cancellation, not during normal operation.</summary>
    /// <param name="disposed">Whether accept reports a disposed socket rather than an aborted socket.</param>
    /// <param name="stopping">Whether the error occurs after shutdown begins.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task NativeLoopbackAcceptShutdownAsync(bool disposed, bool stopping)
    {
        Exception failure = disposed ? new ObjectDisposedException(nameof(Socket)) : new SocketException((int)SocketError.OperationAborted);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new Loopback((_, _) => Task.FromResult(Response(200, [])), accept: async (_, token) =>
        {
            entered.TrySetResult();
            if (stopping)
            {
                using CancellationTokenRegistration registration = token.Register(() => release.TrySetResult());
                await release.Task;
            }

            throw failure;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (stopping)
        {
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            // The synchronous accept failure is already recorded before DisposeAsync cancels.
            Exception? actual = await Record.ExceptionAsync(() => server.DisposeAsync().AsTask());
            Assert.Same(failure, actual);
        }
    }

    /// <summary>Shutdown must not hide response-handler failures with the same exception types.</summary>
    /// <param name="disposed">Whether the handler fails with a disposed socket.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeLoopbackResponseFailureDuringStopAsync(bool disposed)
    {
        Exception failure = disposed ? new ObjectDisposedException(nameof(Socket)) : new SocketException((int)SocketError.OperationAborted);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new Loopback(async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            throw failure;
        });
        using var client = new TcpClient();
        await client.ConnectAsync(server.Scope.Host, server.Scope.Port);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /tiles/0.pbf HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task stopping = server.DisposeAsync().AsTask();
        release.TrySetResult();
        Exception? actual = await Record.ExceptionAsync(() => stopping.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Same(failure, actual);
    }

    /// <summary>A listener with a real pending accept can be shut down without a client.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task NativeLoopbackPendingAcceptStopsAsync()
    {
        var server = new Loopback((_, _) => Task.FromResult(Response(200, [])));
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, server.Count);
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

    private sealed class UnreadKeepAlive : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private int _accepted;
        private int _closed;
        private int _writes;

        internal UnreadKeepAlive(int status)
        {
            _listener.Start();
            Scope = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tiles/");
            _run = RunAsync(status);
        }

        internal Uri Scope { get; }

        internal int Accepted => Volatile.Read(ref _accepted);

        internal int ClosedUnread => Volatile.Read(ref _closed);

        internal int DelayedBodyWrites => Volatile.Read(ref _writes);

        internal int UnreadConnections => 1 - ClosedUnread;

        internal TaskCompletionSource FirstClosed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleaseBody { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource DelayedBodyFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            ReleaseBody.TrySetResult();
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

        private static async Task ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
            {
            }
        }

        private async Task RunAsync(int status)
        {
            using TcpClient first = await _listener.AcceptTcpClientAsync(_stop.Token);
            Interlocked.Increment(ref _accepted);
            await using NetworkStream stream = first.GetStream();
            await ReadRequestAsync(stream, _stop.Token);
            string extra = status == 302 ? "Location: next.pbf\r\n" : status == 503 ? "Retry-After: 0\r\n" : string.Empty;
            byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} fixture\r\nContent-Type: text/plain\r\nContent-Length: 64\r\nConnection: keep-alive\r\n{extra}\r\n");
            await stream.WriteAsync(headers, _stop.Token);
            Task closed = MonitorClosedAsync(stream);
            if (status != 200)
            {
                using TcpClient next = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _accepted);
                await using NetworkStream nextStream = next.GetStream();
                await ReadRequestAsync(nextStream, _stop.Token);
                await nextStream.WriteAsync(Response(200, [1]), _stop.Token);
            }

            await closed;
            await ReleaseBody.Task.WaitAsync(_stop.Token);
            if (ClosedUnread == 0)
            {
                Interlocked.Increment(ref _writes);
                await stream.WriteAsync(new byte[64], _stop.Token);
            }

            DelayedBodyFinished.TrySetResult();
        }

        private async Task MonitorClosedAsync(NetworkStream stream)
        {
            byte[] one = new byte[1];
            try
            {
                int read = await stream.ReadAsync(one, _stop.Token);
                if (read != 0)
                {
                    throw new InvalidOperationException("Unexpected request bytes on withheld response connection.");
                }
            }
            catch (IOException)
            {
                // A reset also proves the handler cannot retain a draining connection.
            }

            Interlocked.Increment(ref _closed);
            FirstClosed.TrySetResult();
        }
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

        internal Loopback(Func<int, string, Task<byte[]>> respond, bool holdOpen = false, Func<TcpListener, CancellationToken, ValueTask<TcpClient>>? accept = null)
        {
            _listener.Start();
            Scope = new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tiles/");
            _run = RunAsync(respond, holdOpen, accept ?? ((listener, token) => listener.AcceptTcpClientAsync(token)));
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
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }

        private async Task RunAsync(Func<int, string, Task<byte[]>> respond, bool holdOpen, Func<TcpListener, CancellationToken, ValueTask<TcpClient>> accept)
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient accepted;
                try
                {
                    accepted = await accept(_listener, _stop.Token);
                }
                catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
                {
                    // Stop can dispose the listener socket before accept observes cancellation.
                    break;
                }
                catch (SocketException) when (_stop.IsCancellationRequested)
                {
                    // Some socket implementations report an aborted accept instead.
                    break;
                }

                using TcpClient client = accepted;
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
