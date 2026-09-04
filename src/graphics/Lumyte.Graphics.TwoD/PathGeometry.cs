namespace Lumyte.Graphics.TwoD;

/// <summary>An immutable path that preserves line, quadratic, and cubic segments.</summary>
public sealed class PathGeometry
{
    internal PathGeometry(PathSegment[] segments, Rect bounds)
    {
        Segments = segments;
        Bounds = bounds;
    }

    public Rect Bounds { get; }
    public bool IsEmpty => Segments.Count == 0;

    internal IReadOnlyList<PathSegment> Segments { get; }
}
