using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

internal static class PathFlattening2D
{
    internal const int MaximumEdges = 65536;

    internal static List<Contour> Flatten(Path2D path, Matrix3x2 transform)
    {
        List<Contour> result = [];
        Contour? current = null;
        Vector2 position = default;
        float scale = MaximumScale(transform);
        float tolerance = 0.125f / MathF.Max(scale, 0.0001f);
        int count = 0;
        foreach (PathSegment2D segment in path.Segments)
        {
            if (current is { Closed: true } && segment.Verb is not PathVerb2D.Move and not PathVerb2D.Close)
            {
                current = new Contour();
                result.Add(current);
                Add(position);
            }

            switch (segment.Verb)
            {
                case PathVerb2D.Move:
                    current = new Contour();
                    result.Add(current);
                    Add(segment.P0);
                    break;
                case PathVerb2D.Line:
                    Add(segment.P0);
                    break;
                case PathVerb2D.Quadratic:
                    Quadratic(position, segment.P0, segment.P1, 0);
                    break;
                case PathVerb2D.Cubic:
                    Cubic(position, segment.P0, segment.P1, segment.P2, 0);
                    break;
                case PathVerb2D.Close:
                    current!.Closed = true;
                    position = current.Points[0];
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(path));
            }
        }

        return result;

        void Add(Vector2 point)
        {
            if (++count > MaximumEdges)
            {
                throw new NotSupportedException("Path flattening exceeds the 65536 edge budget.");
            }

            current!.Points.Add(point);
            position = point;
        }

        void Quadratic(Vector2 p0, Vector2 p1, Vector2 p2, int depth)
        {
            if (Distance(p1, p0, p2) <= tolerance)
            {
                Add(p2);
                return;
            }

            if (depth >= 24)
            {
                throw new NotSupportedException("Curve subdivision exceeds the precision budget.");
            }

            Vector2 a = (p0 + p1) * 0.5f;
            Vector2 b = (p1 + p2) * 0.5f;
            Vector2 midpoint = (a + b) * 0.5f;
            Quadratic(p0, a, midpoint, depth + 1);
            Quadratic(midpoint, b, p2, depth + 1);
        }

        void Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int depth)
        {
            if (MathF.Max(Distance(p1, p0, p3), Distance(p2, p0, p3)) <= tolerance)
            {
                Add(p3);
                return;
            }

            if (depth >= 24)
            {
                throw new NotSupportedException("Curve subdivision exceeds the precision budget.");
            }

            Vector2 a = (p0 + p1) * 0.5f;
            Vector2 b = (p1 + p2) * 0.5f;
            Vector2 c = (p2 + p3) * 0.5f;
            Vector2 d = (a + b) * 0.5f;
            Vector2 e = (b + c) * 0.5f;
            Vector2 f = (d + e) * 0.5f;
            Cubic(p0, a, d, f, depth + 1);
            Cubic(f, e, c, p3, depth + 1);
        }
    }

    private static float MaximumScale(Matrix3x2 transform)
    {
        double squared = ((double)transform.M11 * transform.M11) + ((double)transform.M12 * transform.M12) + ((double)transform.M21 * transform.M21) + ((double)transform.M22 * transform.M22);
        return (float)Math.Min(float.MaxValue, Math.Sqrt(squared));
    }

    private static float Distance(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 delta = end - start;
        float lengthSquared = delta.LengthSquared();
        if (lengthSquared == 0)
        {
            return Vector2.Distance(point, start);
        }

        float projection = Math.Clamp(Vector2.Dot(point - start, delta) / lengthSquared, 0, 1);
        return Vector2.Distance(point, start + (projection * delta));
    }

    internal sealed class Contour
    {
        internal List<Vector2> Points { get; } = [];

        internal bool Closed { get; set; }
    }
}
