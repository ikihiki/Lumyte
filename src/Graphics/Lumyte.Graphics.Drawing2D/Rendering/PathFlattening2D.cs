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

    internal static List<Vector4> Stroke(List<Contour> contours, StrokeStyle2D style, Matrix3x2 transform)
    {
        List<Vector4> triangles = [];
        float half = style.Width * 0.5f;
        float scale = MaximumScale(transform);
        int divisions = 8;
        if (style.Cap == LineCap2D.Round || style.Join == LineJoin2D.Round)
        {
            double requiredDivisions = Math.Ceiling(Math.PI / Math.Acos(Math.Clamp(1 - (0.125 / Math.Max((double)half * scale, 0.125)), -1, 1)));
            if (!double.IsFinite(requiredDivisions) || requiredDivisions > 4096)
            {
                throw new NotSupportedException("Stroke round join exceeds the subdivision budget.");
            }

            divisions = Math.Max(8, (int)requiredDivisions);
        }

        foreach (Contour contour in contours)
        {
            List<Vector2> points = [];
            foreach (Vector2 point in contour.Points)
            {
                if (points.Count == 0 || point != points[^1])
                {
                    points.Add(point);
                }
            }

            if (contour.Closed && points.Count > 1 && points[0] == points[^1])
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 2)
            {
                if (points.Count == 1 && style.Cap == LineCap2D.Round)
                {
                    Disc(points[0]);
                }
                else if (points.Count == 1 && style.Cap == LineCap2D.Square)
                {
                    Vector2 center = points[0];
                    Triangle(center + new Vector2(-half, -half), center + new Vector2(half, -half), center + new Vector2(half, half));
                    Triangle(center + new Vector2(-half, -half), center + new Vector2(half, half), center + new Vector2(-half, half));
                }

                continue;
            }

            int segments = contour.Closed ? points.Count : points.Count - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                var direction = Vector2.Normalize(b - a);
                Vector2 normal = new Vector2(-direction.Y, direction.X) * half;
                if (!contour.Closed && style.Cap == LineCap2D.Square)
                {
                    if (i == 0)
                    {
                        a -= direction * half;
                    }

                    if (i == segments - 1)
                    {
                        b += direction * half;
                    }
                }

                Triangle(a + normal, a - normal, b + normal);
                Triangle(b + normal, a - normal, b - normal);
            }

            for (int i = contour.Closed ? 0 : 1; i < (contour.Closed ? points.Count : points.Count - 1); i++)
            {
                Vector2 center = points[i];
                var before = Vector2.Normalize(center - points[(i + points.Count - 1) % points.Count]);
                var after = Vector2.Normalize(points[(i + 1) % points.Count] - center);
                if (style.Join == LineJoin2D.Round)
                {
                    Disc(center);
                    continue;
                }

                float turn = Cross(before, after);
                if (MathF.Abs(turn) < 0.000001f)
                {
                    continue;
                }

                float sign = turn > 0 ? -1 : 1;
                Vector2 n0 = new Vector2(-before.Y, before.X) * (half * sign);
                Vector2 n1 = new Vector2(-after.Y, after.X) * (half * sign);
                Vector2 a = center + n0;
                Vector2 b = center + n1;
                if (style.Join == LineJoin2D.Miter)
                {
                    Vector2 miter = a + (before * (Cross(b - a, after) / turn));
                    if (Vector2.Distance(miter, center) <= style.MiterLimit * half)
                    {
                        Triangle(a, miter, center);
                        Triangle(miter, b, center);
                        continue;
                    }
                }

                Triangle(a, b, center);
            }

            if (!contour.Closed && style.Cap == LineCap2D.Round)
            {
                Disc(points[0]);
                Disc(points[^1]);
            }
        }

        return triangles;

        void Triangle(Vector2 a, Vector2 b, Vector2 c)
        {
            if (triangles.Count >= MaximumEdges * 3)
            {
                throw new NotSupportedException("Stroke exceeds the triangle budget.");
            }

            triangles.Add(new(a, 1, 0));
            triangles.Add(new(b, 1, 0));
            triangles.Add(new(c, 1, 0));
        }

        void Disc(Vector2 center)
        {
            for (int i = 0; i < divisions; i++)
            {
                float a = MathF.Tau * i / divisions;
                float b = MathF.Tau * (i + 1) / divisions;
                Triangle(center, center + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * half), center + (new Vector2(MathF.Cos(b), MathF.Sin(b)) * half));
            }
        }
    }

    private static float MaximumScale(Matrix3x2 transform)
    {
        double squared = ((double)transform.M11 * transform.M11) + ((double)transform.M12 * transform.M12) + ((double)transform.M21 * transform.M21) + ((double)transform.M22 * transform.M22);
        return (float)Math.Min(float.MaxValue, Math.Sqrt(squared));
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

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
