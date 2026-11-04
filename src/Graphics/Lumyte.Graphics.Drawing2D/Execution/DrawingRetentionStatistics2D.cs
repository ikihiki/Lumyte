namespace Lumyte.Graphics.Drawing2D;

/// <summary>A cumulative snapshot of renderer-issued retained compilation and cache work.</summary>
/// <param name="CompiledRuns">The number of newly prepared page groups, including custom renderer work.</param>
/// <param name="ReusedRuns">The number of exclusively leased resident page groups.</param>
/// <param name="ActiveFrames">The number of busy or quarantined frame slots.</param>
/// <param name="CachedAssets">The number of idle resident page groups.</param>
/// <param name="CachedBytes">The conservative estimated private bytes of idle assets.</param>
public readonly record struct DrawingRetentionStatistics2D(long CompiledRuns, long ReusedRuns, int ActiveFrames, int CachedAssets, long CachedBytes);
