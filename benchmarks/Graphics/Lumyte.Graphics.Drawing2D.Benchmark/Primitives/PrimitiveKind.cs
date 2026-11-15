namespace Lumyte.Graphics.Drawing2D.Benchmark.Primitives;

/// <summary>Additional real default command renderer entry points.</summary>
public enum PrimitiveKind
{
    /// <summary>Analytic rounded rectangle.</summary>
    RoundedRectangle,

    /// <summary>Analytic circle.</summary>
    Circle,

    /// <summary>Stroked line.</summary>
    Line,

    /// <summary>Curved stroked path.</summary>
    StrokedPath,

    /// <summary>Sampled color image.</summary>
    Image,

    /// <summary>Sampled R8 coverage mask.</summary>
    CoverageMask,

    /// <summary>Sampled signed distance field.</summary>
    DistanceField,

    /// <summary>Explicit antialiased triangle mesh.</summary>
    Mesh,

    /// <summary>Direct paint graph evaluation.</summary>
    PaintGraph,
}
