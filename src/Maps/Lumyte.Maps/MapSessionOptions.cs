namespace Lumyte.Maps;

/// <summary>Bounded local finite-set session admission settings.</summary>
public sealed record MapSessionOptions
{
    /// <summary>Gets a value indicating whether the supplied finite tile universe is selected by viewport and integer source zoom.</summary>
    /// <remarks>False preserves fixed-set admission. True loads visible known tiles only and retains adopted tiles until disposal; no LRU, prefetch or HTTP.</remarks>
    public bool SelectVisibleTiles { get; init; }

    /// <summary>Gets the maximum supplied finite tile count.</summary>
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
