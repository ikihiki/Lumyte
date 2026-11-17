namespace Lumyte.Maps;

/// <summary>Bounded local finite-set session admission settings.</summary>
public sealed record MapSessionOptions
{
    /// <summary>Gets a value indicating whether the supplied finite tile universe is selected by viewport and integer source zoom.</summary>
    /// <remarks>False preserves fixed-set admission. True loads visible known tiles only and retains adopted tiles until disposal; no LRU, prefetch or HTTP.</remarks>
    public bool SelectVisibleTiles { get; init; }

    /// <summary>Gets the maximum adopted, in-flight and retiring tile slots in spatial sessions.</summary>
    public int MaxResidentTiles { get; init; } = 64;

    /// <summary>Gets the spatial prefetch ring radius in tiles; zero disables prefetch.</summary>
    public int PrefetchRadius { get; init; } = 1;

    /// <summary>Gets the maximum prefetch candidates admitted after visible and fallback demand.</summary>
    public int MaxPrefetchTiles { get; init; } = 8;

    /// <summary>Gets the maximum same-content ancestor levels requested and used for fallback.</summary>
    public int MaxFallbackDepth { get; init; } = 1;

    /// <summary>Gets the maximum finite input count or spatial demand candidates per camera update.</summary>
    public int MaxPendingTiles { get; init; } = 128;

    /// <summary>Gets the maximum terminally-owned retain operations.</summary>
    public int MaxInFlightTiles { get; init; } = 4;

    /// <summary>Gets completion slots reserved before dispatch.</summary>
    public int MaxCompletedTiles { get; init; } = 4;

    /// <summary>Gets the adoption limit per Advance.</summary>
    public int MaxPublicationsPerAdvance { get; init; } = 2;

    /// <summary>Gets the conservative per-record CPU conversion reservation, separate from tile residency.</summary>
    public long MaxRecordingCpuBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the CPU storage and build reservation budget.</summary>
    public long MaxResidentCpuBytes { get; init; } = 128 * 1024 * 1024;
}
