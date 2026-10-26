namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable contour snapshot containing lines and quadratic or cubic curves.</summary>
public sealed class Path2D
{
    internal Path2D(IEnumerable<PathSegment2D> segments)
    {
        Segments = Array.AsReadOnly(segments.ToArray());
    }

    /// <summary>Gets the immutable segment sequence.</summary>
    public IReadOnlyList<PathSegment2D> Segments { get; }
}
