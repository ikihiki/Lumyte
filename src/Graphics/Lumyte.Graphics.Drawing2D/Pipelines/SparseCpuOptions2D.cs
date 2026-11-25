namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Immutable explicit CPU sparse geometry limits.</summary>
/// <param name="MaxCachedBytes">Maximum idle geometry storage.</param>
/// <param name="MaxCachedAssets">Maximum idle geometry entries.</param>
/// <param name="MaxStrips">Maximum strips per compatible batch.</param>
/// <param name="MaxBoundaryMasks">Maximum boundary pixel masks per compatible batch.</param>
public sealed record SparseCpuOptions2D(long MaxCachedBytes, int MaxCachedAssets, int MaxStrips, int MaxBoundaryMasks)
{
    internal SparseCpuOptions2D Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxCachedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxCachedAssets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStrips);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxBoundaryMasks);
        return this;
    }
}
