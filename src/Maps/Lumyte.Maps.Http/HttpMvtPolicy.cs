using System.Globalization;

namespace Lumyte.Maps.Http;

/// <summary>Immutable native HTTP policy. Identity contains only nonsecret configuration.</summary>
public sealed class HttpMvtPolicy
{
    /// <summary>Initializes a new instance of the <see cref="HttpMvtPolicy"/> class with finite limits.</summary>
    /// <param name="scope">Allowed absolute HTTP(S) directory URI, including port; no query or credentials.</param>
    /// <param name="accessPartition">Nonsecret content-access partition, never a token or token hash.</param>
    /// <param name="maxWireBytes">Counted transport body limit.</param>
    /// <param name="maxExpandedBytes">Decompressed body limit.</param>
    /// <param name="maxEntries">Positive and negative metadata entry limit.</param>
    /// <param name="maxResidentBytes">Cache, pinned old snapshots, active buffers and scratch reservation limit.</param>
    /// <param name="maxInFlight">Maximum admitted concurrent operations; excess admission fails without queuing.</param>
    /// <param name="freshness">Local positive validation interval.</param>
    /// <param name="negativeTtl">Local missing-tile interval.</param>
    /// <param name="deadline">Total deadline including reads, retries and delays.</param>
    /// <param name="maxRetries">Selected 429/500/502/503/504 retry count.</param>
    /// <param name="maxRetryAfter">Maximum acceptable Retry-After.</param>
    /// <param name="maxRedirects">Manual redirects, confined to the same origin and scope.</param>
    public HttpMvtPolicy(Uri scope, string accessPartition, int maxWireBytes = 4194304, int maxExpandedBytes = 4194304, int maxEntries = 128, long maxResidentBytes = 67108864, int maxInFlight = 4, TimeSpan? freshness = null, TimeSpan? negativeTtl = null, TimeSpan? deadline = null, int maxRetries = 2, TimeSpan? maxRetryAfter = null, int maxRedirects = 3)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessPartition);
        if (!scope.IsAbsoluteUri || scope.Scheme is not ("http" or "https") || scope.AbsoluteUri.Length > 2048 || scope.UserInfo.Length != 0 || scope.Query.Length != 0 || scope.Fragment.Length != 0 || scope.AbsolutePath.Contains('%') || scope.OriginalString.Contains('\\') || !scope.AbsolutePath.EndsWith('/'))
        {
            throw new ArgumentException("An HTTP(S) directory scope without credentials or query is required.", nameof(scope));
        }

        if (accessPartition.Length > 128 || maxWireBytes <= 0 || maxExpandedBytes <= 0 || maxExpandedBytes == int.MaxValue || maxEntries is <= 0 or > 4096 || maxInFlight is <= 0 or > 256 || maxRetries is < 0 or > 8 || maxRedirects is < 0 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWireBytes));
        }

        Scope = scope;
        AccessPartition = accessPartition;
        MaxWireBytes = maxWireBytes;
        MaxExpandedBytes = maxExpandedBytes;
        MaxEntries = maxEntries;
        MaxResidentBytes = maxResidentBytes;
        MaxInFlight = maxInFlight;
        Freshness = freshness ?? TimeSpan.FromSeconds(30);
        NegativeTtl = negativeTtl ?? TimeSpan.FromSeconds(5);
        Deadline = deadline ?? TimeSpan.FromSeconds(20);
        MaxRetries = maxRetries;
        MaxRetryAfter = maxRetryAfter ?? TimeSpan.FromSeconds(5);
        MaxRedirects = maxRedirects;
        if (Freshness <= TimeSpan.Zero || NegativeTtl <= TimeSpan.Zero || Freshness > TimeSpan.FromDays(1) || NegativeTtl > TimeSpan.FromDays(1) || Deadline <= TimeSpan.Zero || Deadline > TimeSpan.FromMinutes(5) || MaxRetryAfter < TimeSpan.Zero || MaxRetryAfter > Deadline || maxResidentBytes < BufferReservation)
        {
            throw new ArgumentOutOfRangeException(nameof(deadline));
        }

        Identity = string.Create(CultureInfo.InvariantCulture, $"http-mvt-v1|{scope.AbsoluteUri.Length}:{scope.AbsoluteUri}|{accessPartition.Length}:{accessPartition}|{maxWireBytes}|{maxExpandedBytes}|{maxEntries}|{maxResidentBytes}|{maxInFlight}|{Freshness.Ticks}|{NegativeTtl.Ticks}|{Deadline.Ticks}|{maxRetries}|{MaxRetryAfter.Ticks}|{maxRedirects}");
    }

    /// <summary>Gets the allowed origin and directory.</summary>
    public Uri Scope { get; }

    /// <summary>Gets the explicitly nonsecret content-access identity.</summary>
    public string AccessPartition { get; }

    /// <summary>Gets the complete nonsecret representation policy identity.</summary>
    public string Identity { get; }

    /// <summary>Gets counted wire bytes.</summary>
    public int MaxWireBytes { get; }

    /// <summary>Gets accepted expanded bytes.</summary>
    public int MaxExpandedBytes { get; }

    /// <summary>Gets maximum metadata entries, including negatives.</summary>
    public int MaxEntries { get; }

    /// <summary>Gets the owned payload and scratch reservation limit.</summary>
    public long MaxResidentBytes { get; }

    /// <summary>Gets admitted operation capacity.</summary>
    public int MaxInFlight { get; }

    /// <summary>Gets positive freshness.</summary>
    public TimeSpan Freshness { get; }

    /// <summary>Gets negative freshness.</summary>
    public TimeSpan NegativeTtl { get; }

    /// <summary>Gets total operation deadline.</summary>
    public TimeSpan Deadline { get; }

    /// <summary>Gets retry count.</summary>
    public int MaxRetries { get; }

    /// <summary>Gets maximum accepted server delay.</summary>
    public TimeSpan MaxRetryAfter { get; }

    /// <summary>Gets redirect count.</summary>
    public int MaxRedirects { get; }

    // Expanded buffer includes cap+1 detection. Reserve decoder/handler scratch conservatively;
    // native networking/TLS runtime internals are not claimed as process-wide bounded memory.
    internal long BufferReservation => MaxExpandedBytes + 1L + 33562624;

    internal void Validate(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Scope.Scheme || uri.IdnHost != Scope.IdnHost || uri.Port != Scope.Port || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith(Scope.AbsolutePath, StringComparison.Ordinal) || uri.AbsolutePath.Contains('%') || uri.OriginalString.Contains('\\'))
        {
            throw new InvalidOperationException("HTTP target is outside the registered scope.");
        }
    }
}
