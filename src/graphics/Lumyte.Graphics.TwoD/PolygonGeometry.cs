using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>An expanded triangle list read by shaders through a normal shader-data buffer.</summary>
public sealed class PolygonGeometry
{
    private readonly Vector2[] vertices;

    public PolygonGeometry(IEnumerable<Vector2> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        this.vertices = vertices.ToArray();
        if (this.vertices.Length < 3 || this.vertices.Length % 3 != 0)
        {
            throw new ArgumentException("A polygon geometry requires a non-empty expanded triangle list.", nameof(vertices));
        }
        if (this.vertices.Any(static value => !float.IsFinite(value.X) || !float.IsFinite(value.Y)))
        {
            throw new ArgumentException("Polygon vertices must be finite.", nameof(vertices));
        }
    }

    public ReadOnlyMemory<Vector2> Vertices => vertices;
    public int TriangleCount => vertices.Length / 3;

    public static PolygonGeometry FromConvexPolygon(IEnumerable<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Vector2[] polygon = points.ToArray();
        if (polygon.Length < 3)
        {
            throw new ArgumentException("A convex polygon requires at least three points.", nameof(points));
        }
        if (polygon.Any(static value => !float.IsFinite(value.X) || !float.IsFinite(value.Y)))
        {
            throw new ArgumentException("Polygon points must be finite.", nameof(points));
        }

        var triangles = new Vector2[checked((polygon.Length - 2) * 3)];
        for (int index = 0; index < polygon.Length - 2; index++)
        {
            triangles[index * 3] = polygon[0];
            triangles[index * 3 + 1] = polygon[index + 1];
            triangles[index * 3 + 2] = polygon[index + 2];
        }
        return new(triangles);
    }
}
