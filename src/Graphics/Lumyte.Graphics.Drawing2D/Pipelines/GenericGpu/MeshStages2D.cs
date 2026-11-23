namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Declares concrete mesh stages without implicit compilation or submission.</summary>
public static class MeshStages2D
{
    /// <summary>Creates a canonical EvaluateAndComposite stage.</summary>
    /// <param name="samples">The fixed correlated sample policy.</param>
    /// <returns>The explicit executable stage.</returns>
    public static MeshEvaluateAndComposite2D EvaluateAndComposite(SamplePolicy2D samples) => new(samples);
}
