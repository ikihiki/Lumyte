using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Creates expanded triangle streams from resolved TrueType outlines.</summary>
internal static class GlyphPolygonFactory
{
    private const int MaximumContours = 256;
    private const int MaximumFlattenedPoints = 16_384;
    private const int MaximumTriangles = 262_144;
    private const int MaximumSubdivisionDepth = 18;

    /// <summary>
    /// Flattens the outline and tessellates its non-zero fill. Output coordinates use the 2D
    /// renderer's y-down convention with the font baseline at zero.
    /// </summary>
    internal static bool TryCreate(
        GlyphOutline outline,
        float tolerance,
        out PolygonGeometry geometry)
    {
        geometry = null!;
        if (outline is null || !float.IsFinite(tolerance) || tolerance <= 0)
        {
            return false;
        }

        ReadOnlySpan<Vector2> sourcePoints = outline.Points;
        ReadOnlySpan<bool> onCurve = outline.OnCurve;
        ReadOnlySpan<int> contourEnds = outline.ContourEnds;
        if (sourcePoints.Length == 0
            || sourcePoints.Length != onCurve.Length
            || contourEnds.Length is 0 or > MaximumContours
            || contourEnds[^1] != sourcePoints.Length - 1)
        {
            return false;
        }

        float coordinateMagnitude = 1;
        foreach (Vector2 point in sourcePoints)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                return false;
            }
            coordinateMagnitude = MathF.Max(
                coordinateMagnitude,
                MathF.Max(MathF.Abs(point.X), MathF.Abs(point.Y)));
        }

        float epsilon = MathF.Max(
            1e-6f,
            MathF.Max(coordinateMagnitude * 1e-7f, tolerance * 1e-5f));
        var contours = new List<List<Vector2>>(contourEnds.Length);
        var flattenedPoints = new List<Vector2>();
        int contourStart = 0;
        foreach (int contourEnd in contourEnds)
        {
            if (contourEnd < contourStart || contourEnd >= sourcePoints.Length)
            {
                return false;
            }

            int count = contourEnd - contourStart + 1;
            var contour = new List<Vector2>(Math.Max(count, 4));
            if (!TryFlattenContour(
                sourcePoints.Slice(contourStart, count),
                onCurve.Slice(contourStart, count),
                tolerance,
                epsilon,
                contour))
            {
                return false;
            }
            contourStart = contourEnd + 1;

            Simplify(contour, epsilon);
            if (contour.Count < 3 || Math.Abs(SignedArea(contour)) <= epsilon * epsilon)
            {
                return false;
            }
            if (flattenedPoints.Count > MaximumFlattenedPoints - contour.Count)
            {
                return false;
            }

            contours.Add(contour);
            flattenedPoints.AddRange(contour);
        }
        if (contourStart != sourcePoints.Length)
        {
            return false;
        }

        var edges = new List<Edge>(flattenedPoints.Count);
        var levels = new List<float>(flattenedPoints.Count);
        foreach (List<Vector2> contour in contours)
        {
            for (int index = 0; index < contour.Count; index++)
            {
                Vector2 start = contour[index];
                Vector2 end = contour[(index + 1) % contour.Count];
                levels.Add(start.Y);
                if (MathF.Abs(end.Y - start.Y) <= epsilon)
                {
                    continue;
                }
                edges.Add(new(start, end));
            }
        }
        if (edges.Count < 2)
        {
            return false;
        }

        levels.Sort();
        int levelCount = CompactSortedLevels(levels, epsilon);
        if (levelCount < 2)
        {
            return false;
        }

        var intersections = new List<Intersection>(edges.Count);
        var triangles = new List<Vector2>();
        double minimumTriangleArea = Math.Max(1e-12, (double)epsilon * epsilon);
        for (int levelIndex = 0; levelIndex < levelCount - 1; levelIndex++)
        {
            float top = levels[levelIndex];
            float bottom = levels[levelIndex + 1];
            if (bottom - top <= epsilon)
            {
                continue;
            }

            double middle = ((double)top + bottom) * 0.5;
            intersections.Clear();
            foreach (Edge edge in edges)
            {
                if (middle <= edge.MinimumY || middle >= edge.MaximumY)
                {
                    continue;
                }
                intersections.Add(new(edge, edge.XAt(middle)));
            }
            if (intersections.Count == 0)
            {
                continue;
            }
            if ((intersections.Count & 1) != 0)
            {
                return false;
            }

            intersections.Sort(static (left, right) => left.MiddleX.CompareTo(right.MiddleX));
            if (!HasStableOrdering(intersections, top, bottom, epsilon))
            {
                // Two flattened edges cross within a slab. Splitting at arbitrary edge
                // intersections is deliberately left to the vector-path fallback.
                return false;
            }

            int winding = 0;
            Edge? leftBoundary = null;
            foreach (Intersection intersection in intersections)
            {
                int previous = winding;
                winding += intersection.Edge.WindingDelta;
                if (previous == 0 && winding != 0)
                {
                    leftBoundary = intersection.Edge;
                }
                else if (previous != 0 && winding == 0)
                {
                    if (leftBoundary is not Edge left
                        || !TryAddTrapezoid(
                            left,
                            intersection.Edge,
                            top,
                            bottom,
                            minimumTriangleArea,
                            triangles))
                    {
                        return false;
                    }
                    leftBoundary = null;
                    if (triangles.Count / 3 > MaximumTriangles)
                    {
                        return false;
                    }
                }
            }
            if (winding != 0 || leftBoundary is not null)
            {
                return false;
            }
        }

        if (triangles.Count < 3)
        {
            return false;
        }

        try
        {
            geometry = new PolygonGeometry(triangles);
            return true;
        }
        catch (ArgumentException)
        {
            geometry = null!;
            return false;
        }
        catch (OverflowException)
        {
            geometry = null!;
            return false;
        }
    }

    private static bool TryFlattenContour(
        ReadOnlySpan<Vector2> points,
        ReadOnlySpan<bool> onCurve,
        float tolerance,
        float epsilon,
        List<Vector2> result)
    {
        int count = points.Length;
        if (count < 2)
        {
            return false;
        }

        int firstOnCurve = -1;
        for (int index = 0; index < count; index++)
        {
            if (onCurve[index])
            {
                firstOnCurve = index;
                break;
            }
        }

        Vector2 start = firstOnCurve >= 0
            ? Flip(points[firstOnCurve])
            : Midpoint(Flip(points[^1]), Flip(points[0]));
        AddDistinct(result, start, epsilon);
        Vector2 current = start;
        Vector2? control = null;
        int begin = firstOnCurve >= 0 ? firstOnCurve + 1 : 0;
        int numberToVisit = firstOnCurve >= 0 ? count - 1 : count;
        for (int offset = 0; offset < numberToVisit; offset++)
        {
            int index = (begin + offset) % count;
            Vector2 point = Flip(points[index]);
            if (onCurve[index])
            {
                if (control is Vector2 quadraticControl)
                {
                    if (!TryFlattenQuadratic(
                        current,
                        quadraticControl,
                        point,
                        tolerance,
                        epsilon,
                        result))
                    {
                        return false;
                    }
                }
                else
                {
                    AddDistinct(result, point, epsilon);
                }
                current = point;
                control = null;
            }
            else
            {
                if (control is Vector2 previousControl)
                {
                    Vector2 implicitPoint = Midpoint(previousControl, point);
                    if (!TryFlattenQuadratic(
                        current,
                        previousControl,
                        implicitPoint,
                        tolerance,
                        epsilon,
                        result))
                    {
                        return false;
                    }
                    current = implicitPoint;
                }
                control = point;
            }
        }

        if (control is Vector2 finalControl)
        {
            if (!TryFlattenQuadratic(
                current,
                finalControl,
                start,
                tolerance,
                epsilon,
                result))
            {
                return false;
            }
        }
        else
        {
            AddDistinct(result, start, epsilon);
        }

        if (result.Count > 1 && NearlyEqual(result[0], result[^1], epsilon))
        {
            result.RemoveAt(result.Count - 1);
        }
        return result.Count <= MaximumFlattenedPoints;
    }

    private static bool TryFlattenQuadratic(
        Vector2 start,
        Vector2 control,
        Vector2 end,
        float tolerance,
        float epsilon,
        List<Vector2> output)
    {
        var pending = new Stack<Quadratic>();
        pending.Push(new(start, control, end, 0));
        while (pending.TryPop(out Quadratic curve))
        {
            if (IsFlat(curve, tolerance))
            {
                AddDistinct(output, curve.End, epsilon);
                if (output.Count > MaximumFlattenedPoints)
                {
                    return false;
                }
                continue;
            }
            if (curve.Depth >= MaximumSubdivisionDepth)
            {
                return false;
            }

            Vector2 startControl = Midpoint(curve.Start, curve.Control);
            Vector2 controlEnd = Midpoint(curve.Control, curve.End);
            Vector2 middle = Midpoint(startControl, controlEnd);
            int depth = curve.Depth + 1;
            pending.Push(new(middle, controlEnd, curve.End, depth));
            pending.Push(new(curve.Start, startControl, middle, depth));
        }
        return true;
    }

    private static bool IsFlat(Quadratic curve, float tolerance)
    {
        Vector2 chord = curve.End - curve.Start;
        double chordLengthSquared = Vector2.Dot(chord, chord);
        double distance;
        if (chordLengthSquared <= double.Epsilon)
        {
            distance = Vector2.Distance(curve.Control, curve.Start);
        }
        else
        {
            double cross = Cross(curve.Start, curve.End, curve.Control);
            distance = Math.Abs(cross) / Math.Sqrt(chordLengthSquared);
        }
        return distance <= tolerance;
    }

    private static void Simplify(List<Vector2> contour, float epsilon)
    {
        if (contour.Count < 3)
        {
            return;
        }

        for (int index = contour.Count - 1; index >= 0; index--)
        {
            int previous = (index + contour.Count - 1) % contour.Count;
            if (NearlyEqual(contour[index], contour[previous], epsilon))
            {
                contour.RemoveAt(index);
            }
        }

        bool changed;
        do
        {
            changed = false;
            for (int index = 0; index < contour.Count && contour.Count >= 3; index++)
            {
                Vector2 previous = contour[(index + contour.Count - 1) % contour.Count];
                Vector2 current = contour[index];
                Vector2 next = contour[(index + 1) % contour.Count];
                double scale = Math.Max(
                    1,
                    Math.Max(Vector2.Distance(previous, current), Vector2.Distance(current, next)));
                if (Math.Abs(Cross(previous, current, next)) <= epsilon * scale
                    && Vector2.Dot(current - previous, current - next) <= epsilon * epsilon)
                {
                    contour.RemoveAt(index);
                    changed = true;
                    break;
                }
            }
        }
        while (changed);
    }

    private static int CompactSortedLevels(List<float> levels, float epsilon)
    {
        int destination = 0;
        for (int source = 0; source < levels.Count; source++)
        {
            float level = levels[source];
            if (destination == 0 || level - levels[destination - 1] > epsilon)
            {
                levels[destination++] = level;
            }
        }
        return destination;
    }

    private static bool HasStableOrdering(
        List<Intersection> intersections,
        float top,
        float bottom,
        float epsilon)
    {
        double inset = Math.Max(
            epsilon,
            ((double)bottom - top) * 1e-6);
        double nearTop = Math.Min(bottom, top + inset);
        double nearBottom = Math.Max(top, bottom - inset);
        for (int index = 1; index < intersections.Count; index++)
        {
            Intersection previous = intersections[index - 1];
            Intersection current = intersections[index];
            if (current.MiddleX - previous.MiddleX <= epsilon)
            {
                return false;
            }
            if (previous.Edge.XAt(nearTop) > current.Edge.XAt(nearTop) + epsilon
                || previous.Edge.XAt(nearBottom) > current.Edge.XAt(nearBottom) + epsilon)
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryAddTrapezoid(
        Edge left,
        Edge right,
        float top,
        float bottom,
        double minimumTriangleArea,
        List<Vector2> triangles)
    {
        var topLeft = new Vector2((float)left.XAt(top), top);
        var topRight = new Vector2((float)right.XAt(top), top);
        var bottomRight = new Vector2((float)right.XAt(bottom), bottom);
        var bottomLeft = new Vector2((float)left.XAt(bottom), bottom);
        if (!IsFinite(topLeft)
            || !IsFinite(topRight)
            || !IsFinite(bottomRight)
            || !IsFinite(bottomLeft)
            || topLeft.X > topRight.X
            || bottomLeft.X > bottomRight.X)
        {
            return false;
        }

        AddTriangle(topLeft, topRight, bottomRight, minimumTriangleArea, triangles);
        AddTriangle(topLeft, bottomRight, bottomLeft, minimumTriangleArea, triangles);
        return true;
    }

    private static void AddTriangle(
        Vector2 first,
        Vector2 second,
        Vector2 third,
        double minimumArea,
        List<Vector2> output)
    {
        double area = Cross(first, second, third);
        if (Math.Abs(area) <= minimumArea)
        {
            return;
        }
        output.Add(first);
        if (area > 0)
        {
            output.Add(second);
            output.Add(third);
        }
        else
        {
            output.Add(third);
            output.Add(second);
        }
    }

    private static void AddDistinct(List<Vector2> points, Vector2 point, float epsilon)
    {
        if (points.Count == 0 || !NearlyEqual(points[^1], point, epsilon))
        {
            points.Add(point);
        }
    }

    private static double SignedArea(List<Vector2> points)
    {
        double area = 0;
        for (int index = 0; index < points.Count; index++)
        {
            Vector2 current = points[index];
            Vector2 next = points[(index + 1) % points.Count];
            area += (double)current.X * next.Y - (double)next.X * current.Y;
        }
        return area * 0.5;
    }

    private static double Cross(Vector2 first, Vector2 second, Vector2 third)
        => ((double)second.X - first.X) * ((double)third.Y - first.Y)
            - ((double)second.Y - first.Y) * ((double)third.X - first.X);

    private static Vector2 Flip(Vector2 point) => new(point.X, -point.Y);
    private static Vector2 Midpoint(Vector2 first, Vector2 second) => (first + second) * 0.5f;
    private static bool IsFinite(Vector2 point) => float.IsFinite(point.X) && float.IsFinite(point.Y);

    private static bool NearlyEqual(Vector2 first, Vector2 second, float epsilon)
        => MathF.Abs(first.X - second.X) <= epsilon
            && MathF.Abs(first.Y - second.Y) <= epsilon;

    private readonly record struct Quadratic(
        Vector2 Start,
        Vector2 Control,
        Vector2 End,
        int Depth);

    private readonly record struct Edge(Vector2 Start, Vector2 End)
    {
        public double MinimumY => Math.Min(Start.Y, End.Y);
        public double MaximumY => Math.Max(Start.Y, End.Y);
        public int WindingDelta => Start.Y < End.Y ? 1 : -1;

        public double XAt(double y)
            => Start.X + (y - Start.Y) * ((double)End.X - Start.X) / ((double)End.Y - Start.Y);
    }

    private readonly record struct Intersection(Edge Edge, double MiddleX);
}
