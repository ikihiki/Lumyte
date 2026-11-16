namespace Lumyte.Maps;

/// <summary>Bounded fixed-set session admission settings.</summary>
public sealed record MapSessionOptions
{
    /// <summary>Gets the maximum undispatched fixed tiles.</summary>
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
