using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Converts resolved TrueType contours into the common two-dimensional path representation.</summary>
internal static class GlyphPathFactory
{
    public static bool TryCreate(
        GlyphOutline outline,
        [NotNullWhen(true)] out PathGeometry? path)
    {
        ArgumentNullException.ThrowIfNull(outline);

        var builder = new PathBuilder();
        ReadOnlySpan<Vector2> points = outline.Points;
        ReadOnlySpan<bool> onCurve = outline.OnCurve;
        int contourStart = 0;
        bool hasDrawableContour = false;

        foreach (int contourEnd in outline.ContourEnds)
        {
            int pointCount = contourEnd - contourStart + 1;
            if (pointCount >= 2)
            {
                AppendContour(builder, points, onCurve, contourStart, pointCount);
                hasDrawableContour = true;
            }
            contourStart = contourEnd + 1;
        }

        if (!hasDrawableContour)
        {
            path = null;
            return false;
        }

        path = builder.Build();
        return true;
    }

    private static void AppendContour(
        PathBuilder builder,
        ReadOnlySpan<Vector2> points,
        ReadOnlySpan<bool> onCurve,
        int start,
        int count)
    {
        int firstOnCurve = -1;
        for (int index = 0; index < count; index++)
        {
            if (GetOnCurve(onCurve, start, count, index))
            {
                firstOnCurve = index;
                break;
            }
        }

        Vector2 first = firstOnCurve >= 0
            ? GetPoint(points, start, count, firstOnCurve)
            : Midpoint(
                GetPoint(points, start, count, count - 1),
                GetPoint(points, start, count, 0));
        builder.MoveTo(ToPathCoordinates(first));

        Vector2? pendingControl = null;
        int begin = firstOnCurve >= 0 ? firstOnCurve + 1 : 0;
        int iterations = firstOnCurve >= 0 ? count - 1 : count;
        for (int offset = 0; offset < iterations; offset++)
        {
            Vector2 point = GetPoint(points, start, count, begin + offset);
            if (GetOnCurve(onCurve, start, count, begin + offset))
            {
                if (pendingControl is Vector2 control)
                {
                    builder.QuadraticTo(ToPathCoordinates(control), ToPathCoordinates(point));
                }
                else
                {
                    builder.LineTo(ToPathCoordinates(point));
                }
                pendingControl = null;
            }
            else
            {
                if (pendingControl is Vector2 control)
                {
                    builder.QuadraticTo(
                        ToPathCoordinates(control),
                        ToPathCoordinates(Midpoint(control, point)));
                }
                pendingControl = point;
            }
        }

        if (pendingControl is Vector2 finalControl)
        {
            builder.QuadraticTo(ToPathCoordinates(finalControl), ToPathCoordinates(first));
        }
        builder.Close();
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }

    private static Vector2 GetPoint(
        ReadOnlySpan<Vector2> points,
        int start,
        int count,
        int index)
        => points[start + PositiveModulo(index, count)];

    private static bool GetOnCurve(
        ReadOnlySpan<bool> onCurve,
        int start,
        int count,
        int index)
        => onCurve[start + PositiveModulo(index, count)];

    private static Vector2 Midpoint(Vector2 left, Vector2 right) => (left + right) * 0.5f;

    private static Vector2 ToPathCoordinates(Vector2 point) => new(point.X, -point.Y);
}
