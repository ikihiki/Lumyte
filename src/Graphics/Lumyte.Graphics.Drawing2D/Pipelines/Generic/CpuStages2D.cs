namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Explicit CPU normalization and downloaded coverage validation factories.</summary>
public static class CpuStages2D
{
    /// <summary>Creates actual CPU geometry, paint and correlated clip normalization.</summary>
    /// <param name="limits">Aggregate finite geometry and tile-reference ceilings.</param>
    /// <returns>The immutable CPU normalization declaration.</returns>
    public static CpuNormalize2D Normalize(SceneLimits2D limits) => new(limits);

    /// <summary>Creates a strict source/grid/finite coverage validator.</summary>
    /// <param name="samples">The required sixteen-sample version-one grid.</param>
    /// <returns>The strict coverage validation declaration.</returns>
    public static CpuValidateCoverage2D ValidateCoverage(SamplePolicy2D samples) => new(samples);
}
