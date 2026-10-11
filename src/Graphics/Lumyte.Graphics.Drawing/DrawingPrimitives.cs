using System.Numerics;

namespace Lumyte.Graphics.Drawing;

/// <summary>Builds CPU-side triangle lists and line lists without allocating GPU resources.</summary>
public static class DrawingPrimitives
{
    /// <summary>Creates a counter-clockwise XY rectangle as two triangles.</summary>
    /// <param name="origin">The minimum XY corner.</param>
    /// <param name="size">The finite, positive width and height.</param>
    /// <param name="color">The linear, straight-alpha vertex color.</param>
    /// <returns>Six vertices with top-left texture coordinates.</returns>
    public static DrawingVertex[] Rectangle(Vector2 origin, Vector2 size, Vector4 color)
    {
        ValidatePoint(origin, nameof(origin));
        ValidatePositive(size.X, nameof(size));
        ValidatePositive(size.Y, nameof(size));
        Vector2 maximum = origin + size;
        ValidatePoint(maximum, nameof(size));
        var bottomLeft = new DrawingVertex(new(origin, 0), color, new(0, 1));
        var bottomRight = new DrawingVertex(new(maximum.X, origin.Y, 0), color, new(1, 1));
        var topRight = new DrawingVertex(new(maximum, 0), color, new(1, 0));
        var topLeft = new DrawingVertex(new(origin.X, maximum.Y, 0), color, new(0, 0));
        return [bottomLeft, bottomRight, topRight, bottomLeft, topRight, topLeft];
    }

    /// <summary>Creates a filled counter-clockwise XY circle as a triangle list.</summary>
    /// <param name="center">The circle center.</param>
    /// <param name="radius">The finite, positive radius.</param>
    /// <param name="segments">The number of triangles, at least three.</param>
    /// <param name="color">The linear, straight-alpha vertex color.</param>
    /// <returns>Three vertices per segment, with disk texture coordinates.</returns>
    public static DrawingVertex[] Circle(Vector2 center, float radius, int segments, Vector4 color)
    {
        ValidatePoint(center, nameof(center));
        ValidatePositive(radius, nameof(radius));
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ValidatePoint(center + new Vector2(radius), nameof(radius));
        ValidatePoint(center - new Vector2(radius), nameof(radius));
        var vertices = new DrawingVertex[checked(segments * 3)];
        var middle = new DrawingVertex(new(center, 0), color, new(0.5f));
        for (int i = 0; i < segments; i++)
        {
            vertices[i * 3] = middle;
            vertices[(i * 3) + 1] = CircleVertex(center, radius, i, segments, color);
            vertices[(i * 3) + 2] = CircleVertex(center, radius, (i + 1) % segments, segments, color);
        }

        return vertices;
    }

    /// <summary>Creates one line-list segment; width follows the backend's line rasterization.</summary>
    /// <param name="start">The finite starting position.</param>
    /// <param name="end">The finite ending position.</param>
    /// <param name="color">The linear, straight-alpha vertex color.</param>
    /// <returns>Two vertices to draw with line topology and the color shader.</returns>
    public static DrawingVertex[] Line(Vector3 start, Vector3 end, Vector4 color)
    {
        ValidatePoint(new(start.X, start.Y), nameof(start));
        ValidatePoint(new(end.X, end.Y), nameof(end));
        if (!float.IsFinite(start.Z) || !float.IsFinite(end.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Line positions must be finite.");
        }

        return [new(start, color, Vector2.Zero), new(end, color, Vector2.One)];
    }

    private static DrawingVertex CircleVertex(Vector2 center, float radius, int index, int segments, Vector4 color)
    {
        float angle = MathF.Tau * index / segments;
        var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        return new(new(center + (direction * radius), 0), color, new(0.5f + (direction.X * 0.5f), 0.5f - (direction.Y * 0.5f)));
    }

    private static void ValidatePoint(Vector2 point, string parameter)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameter, "Position must be finite.");
        }
    }

    private static void ValidatePositive(float value, string parameter)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameter, "Size must be finite and positive.");
        }
    }
}
