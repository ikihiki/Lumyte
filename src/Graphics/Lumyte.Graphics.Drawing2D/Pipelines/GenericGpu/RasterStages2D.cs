namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Declares concrete raster stages without implicit compilation or submission.</summary>
public static class RasterStages2D
{
    /// <summary>Creates a canonical Compose stage.</summary>
    /// <param name="samples">The fixed correlated sample policy.</param>
    /// <returns>The explicit executable stage.</returns>
    public static RasterCompose2D Compose(SamplePolicy2D samples) => new(samples);

    /// <summary>Creates a canonical EvaluateAndComposite stage.</summary>
    /// <param name="samples">The fixed correlated sample policy.</param>
    /// <returns>The explicit executable stage.</returns>
    public static RasterEvaluateAndComposite2D EvaluateAndComposite(SamplePolicy2D samples) => new(samples);

    /// <summary>Composes complete correlated pixels through bounded independent native windows.</summary>
    /// <param name="samples">The unchanged correlated sample policy.</param>
    /// <param name="limits">The finite window inventory.</param>
    /// <returns>The source-compatible canonical-port stage.</returns>
    public static PagedRasterCompose2D PagedCompose(SamplePolicy2D samples, GenericPagingLimits2D limits) => new(samples, limits);
}
