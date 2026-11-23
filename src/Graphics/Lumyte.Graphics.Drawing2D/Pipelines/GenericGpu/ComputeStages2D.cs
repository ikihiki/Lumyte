namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Declares concrete compute stages without implicit compilation or submission.</summary>
public static class ComputeStages2D
{
    /// <summary>Creates a canonical Coverage stage.</summary>
    /// <param name="samples">The fixed correlated sample policy.</param>
    /// <returns>The explicit executable stage.</returns>
    public static ComputeCoverage2D Coverage(SamplePolicy2D samples) => new(samples);

    /// <summary>Creates a canonical Paint stage.</summary>
    /// <param name="samples">The fixed correlated sample policy.</param>
    /// <returns>The explicit executable stage.</returns>
    public static ComputePaint2D Paint(SamplePolicy2D samples) => new(samples);

    /// <summary>Creates a canonical Clip stage.</summary>
    /// <param name="samples">The fixed correlated sample policy.</param>
    /// <returns>The explicit executable stage.</returns>
    public static ComputeClip2D Clip(SamplePolicy2D samples) => new(samples);

    /// <summary>Creates explicit bounded scene-page and dense-window paint evaluation.</summary>
    /// <param name="samples">The unchanged correlated sample policy.</param>
    /// <param name="limits">The finite page and window inventory.</param>
    /// <returns>The source-compatible canonical-port stage.</returns>
    public static PagedComputePaint2D PagedPaint(SamplePolicy2D samples, GenericPagingLimits2D limits) => new(samples, limits);

    /// <summary>Creates explicit bounded scene-page and dense-window clip evaluation.</summary>
    /// <param name="samples">The unchanged correlated sample policy.</param>
    /// <param name="limits">The finite page and window inventory.</param>
    /// <returns>The source-compatible canonical-port stage.</returns>
    public static PagedComputeClip2D PagedClip(SamplePolicy2D samples, GenericPagingLimits2D limits) => new(samples, limits);

    /// <summary>Creates explicit bounded scene-page and dense-window coverage evaluation.</summary>
    /// <param name="samples">The unchanged correlated sample policy.</param>
    /// <param name="limits">The finite page and window inventory.</param>
    /// <returns>The source-compatible canonical-port stage.</returns>
    public static PagedComputeCoverage2D PagedCoverage(SamplePolicy2D samples, GenericPagingLimits2D limits) => new(samples, limits);
}
