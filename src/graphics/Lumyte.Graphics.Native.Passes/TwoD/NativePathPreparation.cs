using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Native.Passes;

/// <summary>Native curve subdivision and stroke expansion, independent of GPU resource ownership.</summary>
internal static class NativePathPreparation
{
    internal sealed record Contour(List<Vector2> Points, bool Closed);

    internal static List<Contour> Flatten(PathGeometry path, float tolerance)
    {
        List<Contour> result = [];
        List<Vector2>? points = null;
        Vector2 previous = default, first = default;
        foreach (PathSegment segment in path.Segments)
        {
            switch (segment.Kind)
            {
                case PathSegmentKind.Move:
                    if (points is not null)
                    { result.Add(new(points, false)); }
                    points = [segment.Point];
                    previous = first = segment.Point;
                    break;
                case PathSegmentKind.Line:
                    points ??= [previous];
                    points.Add(segment.Point);
                    previous = segment.Point;
                    break;
                case PathSegmentKind.Quadratic:
                    points ??= [previous];
                    Cubic(points, previous, previous + (segment.Control0 - previous) * (2f / 3),
                        segment.Point + (segment.Control0 - segment.Point) * (2f / 3), segment.Point, tolerance, 0);
                    previous = segment.Point;
                    break;
                case PathSegmentKind.Cubic:
                    points ??= [previous];
                    Cubic(points, previous, segment.Control0, segment.Control1, segment.Point, tolerance, 0);
                    previous = segment.Point;
                    break;
                case PathSegmentKind.Close:
                    if (points is not null)
                    { result.Add(new(points, true)); points = null; }
                    previous = first;
                    break;
            }
        }
        if (points is not null)
        { result.Add(new(points, false)); }
        return result;
    }

    private static void Cubic(List<Vector2> output, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float tolerance, int depth)
    {
        Vector2 chord = d - a;
        float distance = MathF.Max(DistanceToLine(b, a, chord), DistanceToLine(c, a, chord));
        if (distance <= tolerance && Vector2.Distance(a, b) + Vector2.Distance(b, c) + Vector2.Distance(c, d)
            <= Vector2.Distance(a, d) + tolerance || depth >= 16)
        { output.Add(d); return; }
        Vector2 ab = (a + b) * .5f, bc = (b + c) * .5f, cd = (c + d) * .5f;
        Vector2 abc = (ab + bc) * .5f, bcd = (bc + cd) * .5f, mid = (abc + bcd) * .5f;
        Cubic(output, a, ab, abc, mid, tolerance, depth + 1);
        Cubic(output, mid, bcd, cd, d, tolerance, depth + 1);
    }

    private static float DistanceToLine(Vector2 point, Vector2 start, Vector2 direction)
        => direction.LengthSquared() < 1e-20f ? Vector2.Distance(point, start) : MathF.Abs(Cross(direction, point - start)) / direction.Length();

    internal static void FillEdges(List<Vector4> output, IEnumerable<Contour> contours, Matrix3x2 transform)
    {
        foreach (Contour contour in contours)
        {
            if (contour.Points.Count < 2)
            { continue; }
            for (int i = 0; i < contour.Points.Count; i++)
            { Edge(output, Vector2.Transform(contour.Points[i], transform), Vector2.Transform(contour.Points[(i + 1) % contour.Points.Count], transform)); }
        }
    }

    internal static void StrokeEdges(List<Vector4> output, List<Contour> contours, StrokeStyle style, float tolerance)
    {
        foreach (Contour original in contours)
        {
            foreach (Contour contour in Dash(original, style))
            {
                List<Vector2> points = [];
                foreach (Vector2 point in contour.Points)
                { if (points.Count == 0 || Vector2.DistanceSquared(points[^1], point) > 1e-14f) { points.Add(point); } }
                if (contour.Closed && points.Count > 1 && points[0] == points[^1])
                { points.RemoveAt(points.Count - 1); }
                float half = style.Width * .5f;
                if (points.Count < 2)
                {
                    if (points.Count == 1 && style.Cap == StrokeCap.Round)
                    { Circle(output, points[0], half, tolerance); }
                    if (points.Count == 1 && style.Cap == StrokeCap.Square)
                    { Polygon(output, [points[0] + new Vector2(-half, -half), points[0] + new Vector2(half, -half), points[0] + new Vector2(half, half), points[0] + new Vector2(-half, half)]); }
                    continue;
                }
                int segments = contour.Closed ? points.Count : points.Count - 1;
                for (int i = 0; i < segments; i++)
                {
                    Vector2 a = points[i], b = points[(i + 1) % points.Count];
                    Vector2 d = Vector2.Normalize(b - a), n = new(-d.Y * half, d.X * half);
                    if (!contour.Closed && style.Cap == StrokeCap.Square)
                    { if (i == 0) { a -= d * half; } if (i == segments - 1) { b += d * half; } }
                    Polygon(output, [a + n, a - n, b - n, b + n]);
                }
                for (int i = contour.Closed ? 0 : 1; i < (contour.Closed ? points.Count : points.Count - 1); i++)
                {
                    Vector2 p = points[i], a = Vector2.Normalize(p - points[(i + points.Count - 1) % points.Count]), b = Vector2.Normalize(points[(i + 1) % points.Count] - p);
                    float turn = Cross(a, b);
                    if (MathF.Abs(turn) < 1e-6f)
                    { continue; }
                    float side = turn > 0 ? -1 : 1;
                    Vector2 n0 = new Vector2(-a.Y, a.X) * (half * side), n1 = new Vector2(-b.Y, b.X) * (half * side);
                    Vector2 outer0 = p + n0, outer1 = p + n1;
                    if (style.Join == StrokeJoin.Round)
                    {
                        float angle = MathF.Atan2(Cross(n0, n1), Vector2.Dot(n0, n1));
                        float from = MathF.Atan2(n0.Y, n0.X);
                        int count = Math.Clamp((int)MathF.Ceiling(MathF.Abs(angle) / (2 * MathF.Acos(Math.Clamp(1 - tolerance / half, -1, 1)))), 2, 512);
                        List<Vector2> fan = [p];
                        for (int step = 0; step <= count; step++)
                        { float theta = from + angle * step / count; fan.Add(p + new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * half); }
                        Polygon(output, fan);
                        continue;
                    }
                    if (style.Join == StrokeJoin.Miter)
                    {
                        Vector2 intersection = outer0 + a * (Cross(outer1 - outer0, b) / turn);
                        if (Vector2.Distance(intersection, p) <= half * style.MiterLimit)
                        { Polygon(output, [p, outer0, intersection, outer1]); continue; }
                    }
                    Polygon(output, [p, outer0, outer1]);
                }
                if (!contour.Closed && style.Cap == StrokeCap.Round)
                { Circle(output, points[0], half, tolerance); Circle(output, points[^1], half, tolerance); }
            }
        }
    }

    private static IEnumerable<Contour> Dash(Contour contour, StrokeStyle style)
    {
        if (style.Dashes.Count == 0)
        { yield return contour; yield break; }
        float cycle = style.Dashes.Sum(), phase = (style.DashOffset % cycle + cycle) % cycle;
        int dash = 0;
        while (phase >= style.Dashes[dash])
        { phase -= style.Dashes[dash]; dash = (dash + 1) % style.Dashes.Count; }
        float remaining = style.Dashes[dash] - phase;
        List<Contour> pieces = [];
        List<Vector2>? run = null;
        int count = contour.Closed ? contour.Points.Count : contour.Points.Count - 1;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = contour.Points[i], b = contour.Points[(i + 1) % contour.Points.Count];
            float length = Vector2.Distance(a, b);
            if (length <= 1e-7f)
            { continue; }
            float used = 0;
            while (used < length - 1e-7f)
            {
                float amount = MathF.Min(remaining, length - used);
                Vector2 start = Vector2.Lerp(a, b, used / length), end = Vector2.Lerp(a, b, (used + amount) / length);
                if ((dash & 1) == 0)
                { run ??= [start]; run.Add(end); }
                used += amount;
                remaining -= amount;
                if (remaining <= 1e-6f)
                {
                    if (run is not null)
                    { pieces.Add(new(run, false)); run = null; }
                    dash = (dash + 1) % style.Dashes.Count;
                    remaining = style.Dashes[dash];
                }
            }
        }
        if (run is not null)
        { pieces.Add(new(run, false)); }
        if (contour.Closed && pieces.Count > 1 && pieces[0].Points[0] == pieces[^1].Points[^1])
        { var joined = pieces[^1].Points; joined.AddRange(pieces[0].Points.Skip(1)); pieces[0] = new(joined, false); pieces.RemoveAt(pieces.Count - 1); }
        foreach (Contour piece in pieces)
        { yield return piece; }
    }

    internal static void Polygon(List<Vector4> output, IReadOnlyList<Vector2> points)
    {
        float area = 0;
        for (int i = 0; i < points.Count; i++)
        { area += Cross(points[i], points[(i + 1) % points.Count]); }
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Count];
            Edge(output, area < 0 ? b : a, area < 0 ? a : b);
        }
    }
    internal static Vector2[] Rectangle(Rect r) => [new(r.X, r.Y), new(r.X + r.Width, r.Y), new(r.X + r.Width, r.Y + r.Height), new(r.X, r.Y + r.Height)];
    private static void Circle(List<Vector4> output, Vector2 center, float radius, float tolerance)
    {
        int count = Math.Clamp((int)MathF.Ceiling(MathF.PI / MathF.Acos(Math.Clamp(1 - tolerance / radius, -1, 1))), 12, 512);
        var points = new Vector2[count];
        for (int i = 0; i < count; i++)
        { float angle = i * MathF.Tau / count; points[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius; }
        Polygon(output, points);
    }
    private static void Edge(List<Vector4> output, Vector2 a, Vector2 b) { if (a != b) { output.Add(new(a.X, a.Y, b.X, b.Y)); } }
    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
