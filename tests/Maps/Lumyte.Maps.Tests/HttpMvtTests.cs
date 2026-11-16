using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Lumyte.Maps.Http;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Tests;

/// <summary>Scripted native transport policy, revision, allocation and Resources integration tests.</summary>
public sealed class HttpMvtTests
{
    private static readonly MapTileId _tile = new(0, 0, 0);
    private static readonly Uri _scope = new("http://localhost:12345/tiles/");

    /// <summary>Manager's initial, snapshot and post-build observations share one download.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task ManagerUsesOneDownloadAndExactRegistrationAsync()
    {
        byte[] bytes = MvtDecoderTests.Tile("roads", 4096, [9, 0, 0, 10, 2, 2]);
        HttpMvtRegistration registration = Registration();
        using var handler = new Script((_, _) => Task.FromResult(Ok(bytes, "\"one\"")));
        using var client = new HttpClient(handler);
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true);
        await using MemoryManager memory = new();
        await using ResourceManager manager = new("http", source, [new MvtResourceStage(registration.Mvt)], memory, new InlineRuntime());
        using ResourceReference<MapTileContent> reference = await manager.RetainAsync(registration.GetKey(_tile));
        using ResourceLease<MapTileContent> lease = reference.Pin();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), lease.Value.SourceRevision);
        Assert.Single(lease.Value.Layers);
        Assert.Equal(1, handler.Count);
        HttpMvtRegistration other = Registration();
        Assert.Equal(registration.GetKey(_tile), other.GetKey(_tile));
        await using ResourceManager mismatch = new("mismatch", source, [new MvtResourceStage(other.Mvt)], memory, new InlineRuntime());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await mismatch.RetainAsync(other.GetKey(_tile)));
        HttpMvtRegistration changed = Registration(new(_scope, "different-access"));
        Assert.NotEqual(registration.GetKey(_tile), changed.GetKey(_tile));
    }

    /// <summary>Conditional validation preserves matching bytes and old snapshots survive replacement.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task RevalidationAndReplacementAreConsistentAsync()
    {
        var clock = new ManualTime();
        int step = 0;
        using var handler = new Script((request, _) =>
        {
            step++;
            if (step == 1)
            {
                Assert.Empty(request.Headers.IfNoneMatch);
                return Task.FromResult(Ok([1, 2], "\"one\""));
            }

            Assert.Equal("\"one\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
            return Task.FromResult(step == 2 ? new(HttpStatusCode.NotModified) : Ok([3, 4], "\"two\""));
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        ResourceId id = registration.GetKey(_tile).Address.Id;
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        await using ResourceSourceSnapshot old = await source.OpenSnapshotAsync(id);
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(old.Revision, await source.GetRevisionAsync(id));
        Assert.Equal(2, handler.Count);
        clock.Advance(TimeSpan.FromSeconds(31));
        await using ResourceSourceSnapshot current = await source.OpenSnapshotAsync(id);
        Assert.NotEqual(old.Revision, current.Revision);
        Assert.Equal(new byte[] { 1, 2 }, await ReadAsync(old));
        Assert.Equal(new byte[] { 3, 4 }, await ReadAsync(current));
        Assert.Equal(current.Revision, await source.GetRevisionAsync(id));
        Assert.Equal(3, handler.Count);
    }

    /// <summary>Missing tiles have a bounded negative lifetime, authorization is fatal, and invalid 304 cannot fabricate revisions.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task MissingAuthorizationAndInvalid304Async()
    {
        var clock = new ManualTime();
        int step = 0;
        using var handler = new Script((_, _) => Task.FromResult(new HttpResponseMessage(++step switch { 1 => HttpStatusCode.NotFound, 2 => HttpStatusCode.Unauthorized, _ => HttpStatusCode.NotModified })));
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        ResourceId id = registration.GetKey(_tile).Address.Id;
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        Assert.Null(await source.GetRevisionAsync(id));
        await Assert.ThrowsAsync<FileNotFoundException>(async () => await source.OpenSnapshotAsync(id));
        Assert.Equal(1, handler.Count);
        clock.Advance(TimeSpan.FromSeconds(6));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.GetRevisionAsync(id));
        Assert.Equal(2, handler.Count);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(id));
        Assert.Equal(3, handler.Count);
    }

    /// <summary>304 ETag must match the exact cached representation.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task RejectsMismatched304Async()
    {
        var clock = new ManualTime();
        using var handler = new Script((_, count) =>
        {
            HttpResponseMessage response = count == 1 ? Ok([1], "\"one\"") : new(HttpStatusCode.NotModified);
            if (count != 1)
            {
                response.Headers.ETag = new("\"two\"");
            }

            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        ResourceId id = registration.GetKey(_tile).Address.Id;
        _ = await source.GetRevisionAsync(id);
        clock.Advance(TimeSpan.FromSeconds(31));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(id));
    }

    /// <summary>Typed delta/date Retry-After delays use the injected clock, not wall-clock sleeps.</summary>
    /// <param name="date">Use a date instead of delta.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterUsesInjectedTimeAsync(bool date)
    {
        var clock = new ManualTime();
        using var handler = new Script((_, count) =>
        {
            HttpResponseMessage response = count == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : Ok([1]);
            if (count == 1)
            {
                response.Headers.RetryAfter = date ? new(clock.GetUtcNow() + TimeSpan.FromSeconds(2)) : new(TimeSpan.FromSeconds(2));
            }

            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        Task<string?> pending = source.GetRevisionAsync(registration.GetKey(_tile).Address.Id).AsTask();
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, handler.Count);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await pending);
        Assert.Equal(2, handler.Count);
    }

    /// <summary>Retry count is finite and unreasonable or malformed delay headers fail without retrying.</summary>
    /// <param name="value">Retry header.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData("999999999")]
    [InlineData("not-a-date")]
    public async Task RejectsUnreasonableRetryAfterAsync(string value)
    {
        using var handler = new Script((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.TryAddWithoutValidation("Retry-After", value);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(registration.GetKey(_tile).Address.Id));
        Assert.Equal(1, handler.Count);
    }

    /// <summary>Same-origin redirects are counted; cross-origin redirects never forward credentials.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task RedirectScopeAndAuthorizationAsync()
    {
        using var handler = new Script((request, count) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (count == 2)
            {
                Assert.Equal("/tiles/0/0/next.pbf", request.RequestUri!.AbsolutePath);
                return Task.FromResult(Ok([1]));
            }

            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("next.pbf", UriKind.Relative);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, new("Bearer", "fixture-only"));
        _ = await source.GetRevisionAsync(registration.GetKey(_tile).Address.Id);
        Assert.Equal(2, handler.Count);
        using var cross = new Script((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new("http://localhost:54321/tiles/no.pbf");
            return Task.FromResult(response);
        });
        using var otherClient = new HttpClient(cross);
        await using var other = HttpMvtSource.BorrowTrustedClient(registration, otherClient, true, new("Bearer", "fixture-only"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await other.GetRevisionAsync(registration.GetKey(_tile).Address.Id));
        Assert.Equal(1, cross.Count);
    }

    /// <summary>Wire and expanded limits apply to streaming reads even with absent or dishonest lengths.</summary>
    /// <param name="encoding">Transport encoding.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData("identity")]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task RejectsExpandedOverflowAsync(string encoding)
    {
        byte[] body = new byte[1025];
        if (encoding != "identity")
        {
            using var compressed = new MemoryStream();
            using (Stream compressor = encoding == "gzip" ? new GZipStream(compressed, CompressionLevel.SmallestSize, true) : new BrotliStream(compressed, CompressionLevel.SmallestSize, true))
            {
                compressor.Write(body);
            }

            body = compressed.ToArray();
        }

        using var handler = new Script((_, _) =>
        {
            HttpResponseMessage response = Ok(body);
            response.Content.Headers.ContentEncoding.Add(encoding);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration(new(_scope, "public", maxExpandedBytes: 1024));
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(registration.GetKey(_tile).Address.Id));
        Assert.Equal(1, handler.Count);
    }

    /// <summary>Actual reads, not only declarations, are counted; truncated/false lengths are rejected.</summary>
    /// <param name="declared">Untrusted length.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task RejectsFalseLengthAsync(long declared)
    {
        using var handler = new Script((_, _) =>
        {
            HttpResponseMessage response = Ok([1, 2]);
            response.Content.Headers.ContentLength = declared;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(registration.GetKey(_tile).Address.Id));
    }

    /// <summary>Deadline, caller cancellation and source stopping reach body reads, with finite admission.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task DeadlineCancellationStopAndAdmissionAsync()
    {
        var clock = new ManualTime();
        using var handler = new Script((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingStream()),
        }));
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration(new(_scope, "public", maxInFlight: 1, deadline: TimeSpan.FromSeconds(5)));

        // Set media type on each response without buffering the stream.
        handler.MediaType = "application/octet-stream";
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        ResourceId id = registration.GetKey(_tile).Address.Id;
        Task<string?> pending = source.GetRevisionAsync(id).AsTask();
        Assert.False(pending.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.GetRevisionAsync(id));
        clock.Advance(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        using var cancel = new CancellationTokenSource();
        pending = source.GetRevisionAsync(id, cancel.Token).AsTask();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        pending = source.GetRevisionAsync(id).AsTask();
        await source.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await source.GetRevisionAsync(id));
    }

    /// <summary>Old pinned payloads are charged when cache replacement reserves a second receive buffer.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task PinnedSnapshotsPreventBudgetBypassAsync()
    {
        var clock = new ManualTime();
        HttpMvtRegistration registration = Registration(new(_scope, "public", maxExpandedBytes: 16, maxResidentBytes: 33562652));
        using var handler = new Script((_, _) => Task.FromResult(Ok([1])));
        using var client = new HttpClient(handler);
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        ResourceId id = registration.GetKey(_tile).Address.Id;
        await using ResourceSourceSnapshot pinned = await source.OpenSnapshotAsync(id);
        clock.Advance(TimeSpan.FromSeconds(31));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.OpenSnapshotAsync(id));
        Assert.Equal(1, handler.Count);
    }

    /// <summary>Scope and metadata are finite and handler trust is never silently inferred.</summary>
    [Fact]
    public void RejectsUnsafeConfiguration()
    {
        Assert.Throws<ArgumentException>(() => new HttpMvtPolicy(new("https://user:secret@localhost/tiles/"), "public"));
        Assert.Throws<ArgumentException>(() => new HttpMvtPolicy(new("https://localhost/tiles"), "public"));
        foreach (string target in new[] { "http://localhost:12345/outside/a", "https://localhost:12345/tiles/a", "http://localhost:12346/tiles/a", "http://localhost:12345/tiles/a?token=x", "http://localhost:12345/tiles/%2fsecret" })
        {
            Assert.Throws<InvalidOperationException>(() => new HttpMvtRegistration("f", "1", new("p"), new(_scope, "public"), new Dictionary<MapTileId, Uri> { [_tile] = new(target) }));
        }

        using var client = new HttpClient();
        Assert.Throws<ArgumentException>(() => HttpMvtSource.BorrowTrustedClient(Registration(), client, false));
    }

    /// <summary>Accepted gzip/br bytes hash identically to the original MVT, and streams keep payload accounting pinned.</summary>
    /// <param name="encoding">Encoding.</param>
    /// <returns>Completion.</returns>
    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task CompressedRevisionAndStreamOwnershipAsync(string encoding)
    {
        byte[] bytes = [1, 2, 3];
        using var compressed = new MemoryStream();
        using (Stream encoder = encoding == "gzip" ? new GZipStream(compressed, CompressionLevel.SmallestSize, true) : new BrotliStream(compressed, CompressionLevel.SmallestSize, true))
        {
            encoder.Write(bytes);
        }

        using var handler = new Script((_, _) =>
        {
            HttpResponseMessage response = Ok(compressed.ToArray());
            response.Content.Headers.ContentEncoding.Add(encoding);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration(new(_scope, "public", maxExpandedBytes: 16, maxResidentBytes: 33562652));
        var clock = new ManualTime();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        ResourceId id = registration.GetKey(_tile).Address.Id;
        ResourceSourceSnapshot snapshot = await source.OpenSnapshotAsync(id);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), snapshot.Revision);
        await using Stream stream = await snapshot.OpenReadAsync();
        await snapshot.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(31));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.GetRevisionAsync(id));
        byte[] read = new byte[3];
        await stream.ReadExactlyAsync(read);
        Assert.Equal(bytes, read);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await snapshot.OpenReadAsync());
    }

    /// <summary>Concurrent observations queue within bounded admission and reuse the completed download.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task ConcurrentObservationsDownloadOnceAsync()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Script((_, _) => response.Task);
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true);
        ResourceId id = registration.GetKey(_tile).Address.Id;
        Task<string?> revision = source.GetRevisionAsync(id).AsTask();
        Task<ResourceSourceSnapshot> snapshot = source.OpenSnapshotAsync(id).AsTask();
        Assert.Equal(1, handler.Count);
        response.SetResult(Ok([1, 2]));
        await using ResourceSourceSnapshot result = await snapshot;
        Assert.Equal(await revision, result.Revision);
        Assert.Equal(1, handler.Count);
    }

    /// <summary>Retry budget ends after the configured count and the total deadline includes server delay.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task RetryBudgetAndTotalDeadlineAsync()
    {
        var clock = new ManualTime();
        using var handler = new Script((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration(new(_scope, "public", maxRetries: 1));
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        Task<string?> pending = source.GetRevisionAsync(registration.GetKey(_tile).Address.Id).AsTask();
        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<HttpRequestException>(async () => await pending);
        Assert.Equal(2, handler.Count);
        HttpMvtRegistration shortDeadline = Registration(new(_scope, "public", deadline: TimeSpan.FromSeconds(2), maxRetryAfter: TimeSpan.FromSeconds(2)));
        await using var shortSource = HttpMvtSource.BorrowTrustedClient(shortDeadline, client, true, timeProvider: clock);
        pending = shortSource.GetRevisionAsync(shortDeadline.GetKey(_tile).Address.Id).AsTask();
        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Equal(3, handler.Count);
    }

    /// <summary>A redirect to another representation cannot reuse a cached endpoint validator.</summary>
    /// <returns>Completion.</returns>
    [Fact]
    public async Task RedirectCannotValidateAnotherRepresentationAsync()
    {
        var clock = new ManualTime();
        using var handler = new Script((request, count) =>
        {
            if (count == 1)
            {
                return Task.FromResult(Ok([1], "\"one\""));
            }

            if (count == 2)
            {
                Assert.Single(request.Headers.IfNoneMatch);
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new("changed.pbf", UriKind.Relative);
                return Task.FromResult(response);
            }

            Assert.Empty(request.Headers.IfNoneMatch);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
        });
        using var client = new HttpClient(handler);
        HttpMvtRegistration registration = Registration();
        await using var source = HttpMvtSource.BorrowTrustedClient(registration, client, true, timeProvider: clock);
        ResourceId id = registration.GetKey(_tile).Address.Id;
        _ = await source.GetRevisionAsync(id);
        clock.Advance(TimeSpan.FromSeconds(31));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await source.GetRevisionAsync(id));
        Assert.Equal(3, handler.Count);
    }

    /// <summary>Endpoint, policy and decoder limits cannot alias; endpoint/negative metadata has finite admission.</summary>
    [Fact]
    public void CompleteIdentityAndMetadataAdmission()
    {
        HttpMvtRegistration baseline = Registration();
        HttpMvtRegistration limits = Registration(new(_scope, "public", maxWireBytes: 8192));
        Assert.NotEqual(baseline.GetKey(_tile), limits.GetKey(_tile));
        var route = new HttpMvtRegistration("fixture", "1", new("http"), new(_scope, "public"), new Dictionary<MapTileId, Uri> { [_tile] = new(_scope, "different.pbf") });
        Assert.NotEqual(baseline.GetKey(_tile), route.GetKey(_tile));
        var profile = new HttpMvtRegistration("fixture", "1", new("http", maxVertices: 100), new(_scope, "public"), new Dictionary<MapTileId, Uri> { [_tile] = new(_scope, "0/0/0.pbf") });
        Assert.NotEqual(baseline.GetKey(_tile), profile.GetKey(_tile));
        Assert.Throws<ArgumentException>(() => new HttpMvtRegistration("fixture", "1", new("http"), new(_scope, "public", maxEntries: 1), new Dictionary<MapTileId, Uri> { [new(1, 0, 0)] = new(_scope, "a.pbf"), [new(1, 1, 0)] = new(_scope, "b.pbf") }));
        Assert.Throws<ArgumentException>(() => new HttpMvtRegistration("fixture", "1", new("tiny", maxInputBytes: 1), new(_scope, "public"), new Dictionary<MapTileId, Uri> { [_tile] = new(_scope, "a.pbf") }));
        var bounded = new BoundedMvtRegistration("bounded", "1", new("tiny", maxInputBytes: 1), [_tile]);
        Assert.Throws<InvalidDataException>(() => bounded.CreateSnapshot(bounded.GetKey(_tile).Address.Id, new byte[2], "revision"));
    }

    private static HttpMvtRegistration Registration(HttpMvtPolicy? policy = null) => new("fixture", "1", new("http"), policy ?? new(_scope, "public"), new Dictionary<MapTileId, Uri> { [_tile] = new(_scope, "0/0/0.pbf") });

    private static HttpResponseMessage Ok(byte[] bytes, string? etag = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new("application/vnd.mapbox-vector-tile");
        if (etag is not null)
        {
            response.Headers.ETag = new(etag);
        }

        return response;
    }

    private static async Task<byte[]> ReadAsync(ResourceSourceSnapshot snapshot)
    {
        await using Stream stream = await snapshot.OpenReadAsync();
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    internal sealed class ManualTime : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan delta)
        {
            _now += delta;
            foreach (Timer timer in _timers.ToArray())
            {
                timer.Fire();
            }
        }

        private sealed class Timer(ManualTime owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset _due = DateTimeOffset.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner._now + dueTime;
                return true;
            }

            public void Dispose() => _due = DateTimeOffset.MaxValue;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void Fire()
            {
                if (_due <= owner._now)
                {
                    _due = DateTimeOffset.MaxValue;
                    callback(state);
                }
            }
        }
    }

    private sealed class Script(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Count { get; private set; }

        internal string? MediaType { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HttpResponseMessage response = await send(request, ++Count);
            if (MediaType is not null)
            {
                response.Content.Headers.ContentType = new(MediaType);
            }

            return response;
        }
    }

    private sealed class InlineRuntime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
