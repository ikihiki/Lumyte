namespace Lumyte.Graphics.Drawing2D;

/// <summary>Bounds retained GPU work and idle compiled assets per renderer.</summary>
public sealed record DrawingRetentionOptions2D
{
    /// <summary>Gets the maximum number of prepared, recorded, or quarantined frames.</summary>
    public int MaxFramesInFlight { get; init; } = 3;

    /// <summary>Gets the maximum number of idle reusable batches.</summary>
    public int MaxCachedAssets { get; init; } = 512;

    /// <summary>Gets the maximum estimated bytes of idle reusable batches.</summary>
    public long MaxCachedBytes { get; init; } = 64 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFramesInFlight, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxCachedAssets);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxCachedBytes);
    }
}
