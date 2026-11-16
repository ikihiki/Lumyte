using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Lumyte.Resources;

namespace Lumyte.Maps.Http;

/// <summary>Native bounded source with shared revision/snapshot cache, conditional revalidation and no disk storage.</summary>
public sealed class HttpMvtSource : IResourceSource, IAsyncDisposable
{
    private readonly HttpMvtRegistration _registration;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly AuthenticationHeaderValue? _authorization;
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private readonly Dictionary<ResourceId, Entry> _cache = [];
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _admitted;
    private long _resident;
    private bool _stopped;

    /// <summary>Initializes a new instance of the <see cref="HttpMvtSource"/> class with redirects, cookies and decompression disabled.</summary>
    /// <param name="registration">Immutable finite tile registration.</param>
    /// <param name="authorization">Optional credential, retained only as a request header; partition identity is supplied separately.</param>
    /// <param name="timeProvider">Clock and timers.</param>
    public HttpMvtSource(HttpMvtRegistration registration, AuthenticationHeaderValue? authorization = null, TimeProvider? timeProvider = null)
        : this(registration, CreateNativeClient(), true, authorization, timeProvider)
    {
    }

    private HttpMvtSource(HttpMvtRegistration registration, HttpClient client, bool ownsClient, AuthenticationHeaderValue? authorization, TimeProvider? timeProvider)
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Strict wire accounting and handler control are unavailable with Browser fetch.");
        }

        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _client = client;
        _ownsClient = ownsClient;
        _authorization = authorization;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Creates a source borrowing a client under an explicit, unverified trusted-handler contract.</summary>
    /// <param name="registration">Registration.</param>
    /// <param name="client">Client with redirects, cookies, automatic decompression and credential injection disabled; no default headers or mutable concurrent configuration.</param>
    /// <param name="trustHandlerContract">Must explicitly acknowledge that the library cannot inspect or verify the handler.</param>
    /// <param name="authorization">Optional header scoped to the allowed origin.</param>
    /// <param name="timeProvider">Clock and timers.</param>
    /// <returns>A source that does not dispose the borrowed client.</returns>
    public static HttpMvtSource BorrowTrustedClient(HttpMvtRegistration registration, HttpClient client, bool trustHandlerContract, AuthenticationHeaderValue? authorization = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!trustHandlerContract || client.DefaultRequestHeaders.Any())
        {
            throw new ArgumentException("An explicit trusted handler contract and empty default headers are required.", nameof(trustHandlerContract));
        }

        return new(registration, client, false, authorization, timeProvider);
    }

    /// <inheritdoc />
    public async ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        Entry entry = await ObserveAsync(id, cancellationToken);
        return entry.Revision;
    }

    /// <inheritdoc />
    public async ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        Entry entry = await ObserveAsync(id, cancellationToken, pin: true);
        if (entry.Bytes is null)
        {
            throw new FileNotFoundException("HTTP tile is missing.");
        }

        try
        {
            return _registration.Mvt.CreateSnapshot(id, entry.Bytes.AsMemory(0, entry.Length), entry.Revision!, () => Unpin(entry));
        }
        catch
        {
            Unpin(entry);
            throw;
        }
    }

    /// <summary>Stops admission and cancels queued/active reads and retry delays. Borrowed clients remain owned by their caller.</summary>
    /// <returns>Completion after all admitted source operations have exited.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _stopped = true;
            if (_admitted == 0)
            {
                _drained.TrySetResult();
            }
        }

        await _stop.CancelAsync();
        await _drained.Task;
        await _serial.WaitAsync();
        try
        {
            lock (_sync)
            {
                foreach (Entry entry in _cache.Values)
                {
                    entry.Cached = false;
                    if (entry.Pins == 0)
                    {
                        _resident -= entry.Bytes?.LongLength ?? 0;
                    }
                }

                _cache.Clear();
            }

            if (_ownsClient)
            {
                _client.Dispose();
            }
        }
        finally
        {
            _serial.Release();
        }
    }

    private static HttpClient CreateNativeClient()
    {
        if (OperatingSystem.IsBrowser())
        {
            throw new PlatformNotSupportedException("Strict native HTTP policy cannot run on Browser fetch.");
        }

        return new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxConnectionsPerServer = 4,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private async Task<Entry> ObserveAsync(ResourceId id, CancellationToken cancellationToken, bool pin = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Uri uri = _registration.Resolve(id);
        HttpMvtPolicy policy = _registration.Policy;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopped, this);
            if (_admitted >= policy.MaxInFlight)
            {
                throw new InvalidOperationException("HTTP in-flight admission capacity exceeded.");
            }

            _admitted++;
        }

        using var deadline = new CancellationTokenSource(policy.Deadline, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token, deadline.Token);
        bool entered = false;
        long reservation = 0;
        try
        {
            await _serial.WaitAsync(linked.Token);
            entered = true;
            _cache.TryGetValue(id, out Entry? previous);
            if (previous is not null && previous.Expires > _time.GetUtcNow())
            {
                return Pin(previous, pin);
            }

            lock (_sync)
            {
                EvictFor(policy.BufferReservation, id);
                if (_resident > policy.MaxResidentBytes - policy.BufferReservation)
                {
                    throw new InvalidOperationException("HTTP resident byte capacity exceeded (including pinned snapshots).");
                }

                reservation = policy.BufferReservation;
                _resident += reservation;
            }

            Entry next = await FetchAsync(uri, previous, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                _resident -= reservation;
                reservation = 0;
                if (!ReferenceEquals(next, previous))
                {
                    if (previous is not null)
                    {
                        previous.Cached = false;
                        if (previous.Pins == 0)
                        {
                            _resident -= previous.Bytes?.LongLength ?? 0;
                        }
                    }

                    _resident += next.Bytes?.LongLength ?? 0;
                }

                next.Cached = true;
                _cache[id] = next;
                return Pin(next, pin);
            }
        }
        finally
        {
            lock (_sync)
            {
                _resident -= reservation;
                _admitted--;
                if (_stopped && _admitted == 0)
                {
                    _drained.TrySetResult();
                }
            }

            if (entered)
            {
                _serial.Release();
            }
        }
    }

    private Entry Pin(Entry entry, bool pin)
    {
        lock (_sync)
        {
            if (pin && entry.Bytes is not null)
            {
                entry.Pins++;
            }

            return entry;
        }
    }

    private void Unpin(Entry entry)
    {
        lock (_sync)
        {
            if (--entry.Pins == 0 && !entry.Cached)
            {
                _resident -= entry.Bytes?.LongLength ?? 0;
            }
        }
    }

    private void EvictFor(long bytes, ResourceId protectedId)
    {
        foreach (ResourceId key in _cache.Keys.ToArray())
        {
            if (_resident <= _registration.Policy.MaxResidentBytes - bytes)
            {
                break;
            }

            Entry entry = _cache[key];
            if (key == protectedId || entry.Pins != 0)
            {
                continue;
            }

            _cache.Remove(key);
            entry.Cached = false;
            _resident -= entry.Bytes?.LongLength ?? 0;
        }
    }

    private async Task<Entry> FetchAsync(Uri initial, Entry? cached, CancellationToken cancellationToken)
    {
        HttpMvtPolicy policy = _registration.Policy;
        Uri uri = initial;
        int redirects = 0;
        int retries = 0;
        while (true)
        {
            policy.Validate(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = _authorization;
            request.Headers.AcceptEncoding.ParseAdd("gzip, br");
            bool conditional = cached?.ETag is not null && cached.RepresentationUri == uri;
            if (conditional)
            {
                request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached!.ETag!));
            }

            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            int status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308)
            {
                if (++redirects > policy.MaxRedirects || response.Headers.Location is not Uri location)
                {
                    throw new InvalidDataException("Redirect capacity or location is invalid.");
                }

                uri = new Uri(uri, location);
                policy.Validate(uri); // Reject cross-origin before constructing another request or forwarding authorization.
                continue;
            }

            if (status == 401 || status == 403)
            {
                throw new UnauthorizedAccessException("HTTP tile authorization failed.");
            }

            if (response.Headers.Contains("ETag") && response.Headers.ETag is null)
            {
                throw new InvalidDataException("Malformed ETag metadata.");
            }

            if (status == 304)
            {
                if (!conditional || cached?.Bytes is null || cached.ETag is null || (response.Headers.ETag is not null && response.Headers.ETag.ToString() != cached.ETag))
                {
                    throw new InvalidDataException("304 does not match a cached representation.");
                }

                cached.Expires = _time.GetUtcNow() + policy.Freshness;
                return cached;
            }

            if (status == 404)
            {
                return new(null, 0, null, null, _time.GetUtcNow() + policy.NegativeTtl, uri);
            }

            if (status is 429 or 500 or 502 or 503 or 504)
            {
                if (retries++ >= policy.MaxRetries)
                {
                    throw new HttpRequestException("HTTP tile retry capacity exhausted.", null, response.StatusCode);
                }

                var delay = TimeSpan.FromMilliseconds(100);
                if (response.Headers.Contains("Retry-After"))
                {
                    RetryConditionHeaderValue retry = response.Headers.RetryAfter ?? throw new InvalidDataException("Invalid Retry-After.");
                    delay = retry.Delta ?? (retry.Date!.Value - _time.GetUtcNow());
                    if (delay < TimeSpan.Zero)
                    {
                        delay = TimeSpan.Zero;
                    }
                }

                if (delay > policy.MaxRetryAfter)
                {
                    throw new InvalidDataException("Retry-After exceeds policy.");
                }

                response.Dispose();
                await Task.Delay(delay, _time, cancellationToken);
                continue;
            }

            if (status != 200)
            {
                throw new HttpRequestException("Unexpected HTTP tile status.", null, response.StatusCode);
            }

            string? media = response.Content.Headers.ContentType?.MediaType;
            if (media is not ("application/vnd.mapbox-vector-tile" or "application/x-protobuf" or "application/octet-stream"))
            {
                throw new InvalidDataException("Unsupported tile content type.");
            }

            string[] encodings = response.Content.Headers.ContentEncoding.ToArray();
            if (encodings.Length > 1 || (encodings.Length == 1 && encodings[0] is not ("gzip" or "br" or "identity")))
            {
                throw new InvalidDataException("Unsupported transport content encoding.");
            }

            long? declared = response.Content.Headers.ContentLength;
            if (declared > policy.MaxWireBytes)
            {
                throw new InvalidDataException("Declared wire length exceeds capacity.");
            }

            await using Stream raw = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var counted = new CountedStream(raw, policy.MaxWireBytes);
            await using Stream expanded = encodings.FirstOrDefault() switch
            {
                "gzip" => new GZipStream(counted, CompressionMode.Decompress, leaveOpen: true),
                "br" => new BrotliStream(counted, CompressionMode.Decompress, leaveOpen: true),
                _ => counted,
            };
            byte[] buffer = new byte[policy.MaxExpandedBytes + 1];
            int length = 0;
            while (true)
            {
                int read = await expanded.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (length > policy.MaxExpandedBytes)
                {
                    throw new InvalidDataException("Expanded tile capacity exceeded.");
                }
            }

            // Drain at most wire cap+1, detecting false lengths and trailing transport data.
            byte[] scratch = new byte[8192];
            while (await counted.ReadAsync(scratch, cancellationToken) != 0)
            {
            }

            if (declared is not null && declared != counted.Count)
            {
                throw new InvalidDataException("Wire length differs from Content-Length.");
            }

            string revision = Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, length)));
            string? etag = response.Headers.ETag?.ToString();
            if (etag?.Length > 1024)
            {
                throw new InvalidDataException("ETag metadata exceeds capacity.");
            }

            return new(buffer, length, revision, etag, _time.GetUtcNow() + policy.Freshness, uri);
        }
    }

    private sealed class Entry(byte[]? bytes, int length, string? revision, string? etag, DateTimeOffset expires, Uri representationUri)
    {
        internal byte[]? Bytes { get; } = bytes;

        internal int Length { get; } = length;

        internal string? Revision { get; } = revision;

        internal string? ETag { get; } = etag;

        internal Uri RepresentationUri { get; } = representationUri;

        internal DateTimeOffset Expires { get; set; } = expires;

        internal bool Cached { get; set; }

        internal int Pins { get; set; }
    }

    private sealed class CountedStream(Stream inner, long limit) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => Count; set => throw new NotSupportedException(); }

        internal long Count { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = inner.Read(buffer, offset, (int)Math.Min(count, limit - Count + 1));
            Charge(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, limit - Count + 1)], cancellationToken);
            Charge(read);
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Charge(int read)
        {
            Count += read;
            if (Count > limit)
            {
                throw new InvalidDataException("Counted wire capacity exceeded.");
            }
        }
    }
}
