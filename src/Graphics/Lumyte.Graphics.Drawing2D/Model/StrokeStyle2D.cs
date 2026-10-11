namespace Lumyte.Graphics.Drawing2D;

/// <summary>Describes a stroke in logical coordinates.</summary>
public sealed record StrokeStyle2D
{
    /// <summary>Gets the finite positive stroke width.</summary>
    public float Width { get; init; } = 1;

    /// <summary>Gets the open-end cap style.</summary>
    public LineCap2D Cap { get; init; } = LineCap2D.Butt;

    /// <summary>Gets the join style.</summary>
    public LineJoin2D Join { get; init; } = LineJoin2D.Miter;

    /// <summary>Gets the finite miter limit, at least one; longer joins become bevels.</summary>
    public float MiterLimit { get; init; } = 4;
}
