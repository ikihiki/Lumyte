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
}
