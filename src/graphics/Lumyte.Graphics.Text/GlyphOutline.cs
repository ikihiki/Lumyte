using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>
/// A resolved TrueType glyph outline in font units. Points retain the font's y-up coordinate system.
/// </summary>
public sealed class GlyphOutline
{
    private readonly Vector2[] points;
    private readonly bool[] onCurve;
    private readonly int[] contourEnds;

    internal GlyphOutline(Vector2[] points, bool[] onCurve, int[] contourEnds)
    {
        if (points.Length != onCurve.Length)
        {
            throw new ArgumentException("Every outline point must have an on-curve flag.", nameof(onCurve));
        }

        this.points = points;
        this.onCurve = onCurve;
        this.contourEnds = contourEnds;
    }

    /// <summary>The outline points in y-up font coordinates.</summary>
    public ReadOnlySpan<Vector2> Points => points;

    /// <summary>Whether each corresponding point lies on the curve.</summary>
    public ReadOnlySpan<bool> OnCurve => onCurve;

    /// <summary>The inclusive final point index of each contour.</summary>
    public ReadOnlySpan<int> ContourEnds => contourEnds;
}
