using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Portable.Passes;

/// <summary>Portable tessellation produces edges, never a CPU-rendered coverage image.</summary>
internal static class PortableDrawingGeometry
{
    internal sealed record Contour(List<Vector2> Points, bool Closed);
    internal static List<Contour> Rectangle(Rect r) => [new([new(r.X, r.Y), new(r.Right, r.Y), new(r.Right, r.Bottom), new(r.X, r.Bottom)], true)];
    internal static List<Contour> Shape(Draw2DShapeCommand shape, float scale)
    {
        Rect r = shape.Bounds;
        if (shape.Kind == Draw2DShapeKind.Rectangle)
        { return Rectangle(r); }
        var p = new List<Vector2>();
        if (shape.Kind == Draw2DShapeKind.Ellipse)
        {
            int count = Math.Clamp((int)MathF.Ceiling(MathF.PI * MathF.Sqrt(MathF.Max(r.Width, r.Height) * MathF.Max(scale, 1) / .08f)), 24, 2048);
            for (int i = 0; i < count; i++)
            { float a = i * MathF.Tau / count; p.Add(new(r.X + r.Width * .5f + MathF.Cos(a) * r.Width * .5f, r.Y + r.Height * .5f + MathF.Sin(a) * r.Height * .5f)); }
        }
        else
        {
            float maximum = MathF.Min(r.Width, r.Height) * .5f;
            var c = shape.CornerRadius;
            Arc(p, new(r.X + MathF.Min(c.TopLeft, maximum), r.Y + MathF.Min(c.TopLeft, maximum)), MathF.Min(c.TopLeft, maximum), MathF.PI, MathF.PI * .5f, scale);
            Arc(p, new(r.Right - MathF.Min(c.TopRight, maximum), r.Y + MathF.Min(c.TopRight, maximum)), MathF.Min(c.TopRight, maximum), -MathF.PI * .5f, MathF.PI * .5f, scale);
            Arc(p, new(r.Right - MathF.Min(c.BottomRight, maximum), r.Bottom - MathF.Min(c.BottomRight, maximum)), MathF.Min(c.BottomRight, maximum), 0, MathF.PI * .5f, scale);
            Arc(p, new(r.X + MathF.Min(c.BottomLeft, maximum), r.Bottom - MathF.Min(c.BottomLeft, maximum)), MathF.Min(c.BottomLeft, maximum), MathF.PI * .5f, MathF.PI * .5f, scale);
        }
        return [new(p, true)];
    }
    internal static List<Contour> Flatten(PathGeometry path, float scale)
    {
        var result = new List<Contour>();
        List<Vector2>? points = null;
        Vector2 current = default;
        float tolerance = .08f / MathF.Max(scale, .001f);
        foreach (var segment in path.Segments)
        {
            if (segment.Kind == PathSegmentKind.Move)
            { points = []; current = segment.Point; points.Add(current); result.Add(new(points, false)); }
            else if (segment.Kind == PathSegmentKind.Close)
            { if (points is not null) { result[^1] = new(points, true); current = points[0]; points = null; } }
            else
            {
                if (points is null)
                { points = [current]; result.Add(new(points, false)); }
                if (segment.Kind == PathSegmentKind.Line)
                { Add(points, segment.Point); }
                else if (segment.Kind == PathSegmentKind.Quadratic)
                { Curve(points, current, current + (segment.Control0 - current) * (2f / 3), segment.Point + (segment.Control0 - segment.Point) * (2f / 3), segment.Point, tolerance, 0); }
                else
                { Curve(points, current, segment.Control0, segment.Control1, segment.Point, tolerance, 0); }
                current = segment.Point;
            }
        }
        return result;
    }
    private static void Curve(List<Vector2> p, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float tolerance, int depth)
    {
        if (depth >= 16 || MathF.Max(Distance(b, a, d), Distance(c, a, d)) <= tolerance)
        { Add(p, d); return; }
        Vector2 ab = (a + b) * .5f, bc = (b + c) * .5f, cd = (c + d) * .5f, abc = (ab + bc) * .5f, bcd = (bc + cd) * .5f, mid = (abc + bcd) * .5f;
        Curve(p, a, ab, abc, mid, tolerance, depth + 1);
        Curve(p, mid, bcd, cd, d, tolerance, depth + 1);
    }
    private static float Distance(Vector2 p, Vector2 a, Vector2 b)
    { Vector2 d = b - a; return Vector2.Distance(p, a + d * Math.Clamp(Vector2.Dot(p - a, d) / MathF.Max(d.LengthSquared(), 1e-20f), 0, 1)); }
    private static void Add(List<Vector2> points, Vector2 point) { if (points.Count == 0 || points[^1] != point) { points.Add(point); } }
    internal static List<Contour> Stroke(List<Contour> input, StrokeStyle style, float scale)
    {
        var result = new List<Contour>();
        foreach (Contour original in input)
        {
            foreach (Contour contour in Dash(original, style))
            {
                var p = contour.Points;
                if (p.Count < 2)
                { continue; }
                bool closed = contour.Closed;
                int n = p.Count;
                if (closed && p[0] == p[^1])
                { n--; }
                if (n < 2)
                { continue; }
                float half = style.Width * .5f;
                int segmentCount = closed ? n : n - 1;
                for (int i = 0; i < segmentCount; i++)
                {
                    Vector2 a = p[i], b = p[(i + 1) % n], d = Vector2.Normalize(b - a), normal = new(-d.Y * half, d.X * half);
                    if (!closed && style.Cap == StrokeCap.Square)
                    { if (i == 0) { a -= d * half; } if (i == segmentCount - 1) { b += d * half; } }
                    Polygon([a + normal, a - normal, b - normal, b + normal]);
                }
                for (int i = closed ? 0 : 1; i < (closed ? n : n - 1); i++)
                {
                    Vector2 before = Vector2.Normalize(p[i] - p[(i + n - 1) % n]), after = Vector2.Normalize(p[(i + 1) % n] - p[i]);
                    float cross = before.X * after.Y - before.Y * after.X, side = cross > 0 ? -1 : 1;
                    Vector2 a = new(-before.Y * side, before.X * side), b = new(-after.Y * side, after.X * side);
                    var join = new List<Vector2> { p[i], p[i] + a * half };
                    float denominator = 1 + Vector2.Dot(before, after);
                    Vector2 miter = (a + b) * (half / MathF.Max(denominator, 1e-20f));
                    if (style.Join == StrokeJoin.Miter && denominator > 1e-6f && miter.Length() <= style.MiterLimit * half)
                    { join.Add(p[i] + miter); }
                    else if (style.Join == StrokeJoin.Round)
                    { Arc(join, p[i], half, MathF.Atan2(a.Y, a.X), MathF.Atan2(a.X * b.Y - a.Y * b.X, Vector2.Dot(a, b)), scale); }
                    join.Add(p[i] + b * half);
                    Polygon(join);
                }
                if (!closed && style.Cap == StrokeCap.Round)
                { Circle(p[0]); Circle(p[n - 1]); }
                void Circle(Vector2 center)
                { var circle = new List<Vector2>(); Arc(circle, center, half, 0, MathF.Tau, scale); Polygon(circle); }
            }
        }
        return result;
        void Polygon(List<Vector2> points)
        {
            float area = 0;
            for (int i = 0; i < points.Count; i++)
            { Vector2 a = points[i], b = points[(i + 1) % points.Count]; area += a.X * b.Y - a.Y * b.X; }
            if (area < 0)
            { points.Reverse(); }
            if (area != 0)
            { result.Add(new(points, true)); }
        }
    }
    private static IEnumerable<Contour> Dash(Contour contour, StrokeStyle style)
    {
        IReadOnlyList<float> pattern = style.Dashes;
        if (pattern.Count == 0)
        { return [contour]; }
        var result = new List<Contour>();
        float total = pattern.Sum(), offset = ((style.DashOffset % total) + total) % total;
        int index = 0;
        while (offset >= pattern[index])
        { offset -= pattern[index]; index = (index + 1) % pattern.Count; }
        float remaining = pattern[index] - offset;
        List<Vector2>? active = null;
        int count = contour.Closed ? contour.Points.Count : contour.Points.Count - 1;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = contour.Points[i], b = contour.Points[(i + 1) % contour.Points.Count], d = b - a;
            float length = d.Length();
            if (length < 1e-8f)
            { continue; }
            d /= length;
            float at = 0;
            while (at < length - 1e-6f)
            {
                float step = MathF.Min(remaining, length - at);
                Vector2 start = a + d * at, end = a + d * (at + step);
                if (index % 2 == 0)
                { active ??= []; Add(active, start); Add(active, end); }
                at += step;
                remaining -= step;
                if (remaining <= 1e-6f)
                {
                    if (active is not null)
                    { result.Add(new(active, false)); active = null; }
                    index = (index + 1) % pattern.Count;
                    remaining = pattern[index];
                }
            }
        }
        if (active is not null)
        { result.Add(new(active, false)); }
        if (contour.Closed && result.Count > 0)
        {
            var first = result[0].Points;
            var last = result[^1].Points;
            Vector2 seam = contour.Points[0];
            if (Vector2.DistanceSquared(first[0], seam) < 1e-8f && Vector2.DistanceSquared(last[^1], seam) < 1e-8f)
            {
                if (result.Count == 1)
                { first.RemoveAt(first.Count - 1); result[0] = new(first, true); }
                else
                { last[^1] = seam; last.AddRange(first.Skip(1)); result[0] = new(last, false); result.RemoveAt(result.Count - 1); }
            }
        }
        return result;
    }
    private static void Arc(List<Vector2> points, Vector2 center, float radius, float start, float sweep, float scale)
    {
        int n = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) * MathF.Sqrt(MathF.Max(radius * scale, .01f) / .08f)), 1, 1024);
        for (int i = 0; i <= n; i++)
        { float a = start + sweep * i / n; Add(points, center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius); }
    }
    internal static Vector4[] Edges(IEnumerable<Contour> contours, Matrix3x2 transform)
    {
        var result = new List<Vector4>();
        foreach (var contour in contours)
        { for (int i = 0; i < contour.Points.Count; i++) { Vector2 a = Vector2.Transform(contour.Points[i], transform), b = Vector2.Transform(contour.Points[(i + 1) % contour.Points.Count], transform); if (a != b) { result.Add(new(a.X, a.Y, b.X, b.Y)); } } }
        return result.ToArray();
    }
    internal static float Scale(Matrix3x2 m) => MathF.Max(new Vector2(m.M11, m.M12).Length(), new Vector2(m.M21, m.M22).Length());
}
