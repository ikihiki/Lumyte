using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>An immutable, backend-independent sequence of vector curves.</summary>
public sealed class PathGeometry
{
    internal PathGeometry(PathSegment[] segments, Rect bounds)
    { Segments = Array.AsReadOnly(segments); Bounds = bounds; }
    public IReadOnlyList<PathSegment> Segments { get; }
    public Rect Bounds { get; }
    public bool IsEmpty => Segments.Count == 0;
}

/// <summary>Triangle vertices in drawing order, independent of any GPU representation.</summary>
public sealed class PolygonGeometry
{
    public PolygonGeometry(IEnumerable<Vector2> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        var copy = vertices.ToArray();
        if (copy.Length == 0 || copy.Length % 3 != 0)
        { throw new ArgumentException("Geometry requires complete triangles.", nameof(vertices)); }
        if (copy.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
        { throw new ArgumentException("Vertices must be finite.", nameof(vertices)); }
        Vertices = Array.AsReadOnly(copy);
    }
    public IReadOnlyList<Vector2> Vertices { get; }
    public static PolygonGeometry FromConvexPolygon(IEnumerable<Vector2> points)
    {
        var copy = points.ToArray();
        if (copy.Length < 3)
        { throw new ArgumentException("A polygon requires three points.", nameof(points)); }
        var triangles = new Vector2[(copy.Length - 2) * 3];
        for (int i = 1; i < copy.Length - 1; i++)
        { triangles[(i - 1) * 3] = copy[0]; triangles[(i - 1) * 3 + 1] = copy[i]; triangles[(i - 1) * 3 + 2] = copy[i + 1]; }
        return new(triangles);
    }
}
