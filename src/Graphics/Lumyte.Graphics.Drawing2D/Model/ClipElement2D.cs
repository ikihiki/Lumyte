using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A geometric clip fixed in the transform active when recorded.</summary>
/// <param name="Path">The immutable contour.</param>
/// <param name="Transform">The captured logical transform.</param>
/// <param name="Rule">The contour fill rule.</param>
public sealed record ClipElement2D(Path2D Path, Matrix3x2 Transform, FillRule2D Rule)
{
    /// <summary>Gets a value indicating whether this rectangle partitions whole pixels by device-space center membership rather than antialiased coverage.</summary>
    public bool IsOwnership { get; init; }
}
