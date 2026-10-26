namespace Lumyte.Graphics.Drawing2D;

/// <summary>Identifies a path segment.</summary>
public enum PathVerb2D
{
    /// <summary>Uses Move behavior.</summary>
    Move,

    /// <summary>Uses Line behavior.</summary>
    Line,

    /// <summary>Uses Quadratic behavior.</summary>
    Quadratic,

    /// <summary>Uses Cubic behavior.</summary>
    Cubic,

    /// <summary>Uses Close behavior.</summary>
    Close,
}
