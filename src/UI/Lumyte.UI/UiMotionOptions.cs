namespace Lumyte.UI;

/// <summary>Application-wide limits covering transitions, opacity caches and GPU-retiring images.</summary>
public sealed record UiMotionOptions
{
    /// <summary>Gets the configured motion policy.</summary>
    public UiMotionPolicy Policy { get; init; } = UiMotionPolicy.Full;

    /// <summary>Gets the positive concurrent transition limit.</summary>
    public int MaximumActiveTransitions { get; init; } = 16;

    /// <summary>Gets the positive aggregate pixel limit for intermediate buffers and snapshots.</summary>
    public long MaximumSnapshotPixels { get; init; } = 8 * 1024 * 1024;

    /// <summary>Gets the positive aggregate RGBA16Float texel payload limit in bytes.</summary>
    public long MaximumSnapshotBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the positive real-time preparation deadline.</summary>
    public TimeSpan PreparationTimeout { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Gets the positive real-time visual lifetime limit.</summary>
    public TimeSpan MaximumVisualLifetime { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (Policy is < UiMotionPolicy.Full or > UiMotionPolicy.Disabled || MaximumActiveTransitions <= 0 || MaximumSnapshotPixels <= 0
            || MaximumSnapshotBytes <= 0 || PreparationTimeout <= TimeSpan.Zero || MaximumVisualLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UiMotionOptions));
        }
    }
}
