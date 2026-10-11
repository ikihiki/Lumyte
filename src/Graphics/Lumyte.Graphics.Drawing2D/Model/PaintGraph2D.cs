using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable paint DAG, built through validated factories and borrowing referenced image resources.</summary>
public sealed class PaintGraph2D
{
    private const int MaximumDepth = 128;
    private const int MaximumExpandedNodes = 65536;
    private readonly int _depth;
    private readonly int _expandedNodes;

    private PaintGraph2D(PaintNode2D root, int depth = 1, int expandedNodes = 1)
    {
        if (depth > MaximumDepth || expandedNodes > MaximumExpandedNodes)
        {
            throw new ArgumentException("The paint graph exceeds the depth or expanded evaluation budget.");
        }

        Root = root;
        _depth = depth;
        _expandedNodes = expandedNodes;
    }

    /// <summary>Gets the immutable public node tree; shared child nodes preserve graph sharing.</summary>
    public PaintNode2D Root { get; }

    /// <summary>Creates a spatially constant straight-alpha sRGB paint.</summary>
    /// <param name="color">The finite color with components between zero and one.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D Solid(Color2D color)
    {
        ModelValidation2D.Color(color, nameof(color));
        return new PaintGraph2D(new SolidPaintNode2D(color));
    }

    /// <summary>Creates a two-point gradient, normalized to the equivalent three-point definition.</summary>
    /// <param name="start">The finite start point.</param>
    /// <param name="end">The distinct finite end point.</param>
    /// <param name="stops">Nondecreasing finite color stops, copied in order.</param>
    /// <param name="extend">The extension rule.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D LinearGradient(Vector2 start, Vector2 end, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend = ExtendMode2D.Pad)
    {
        ModelValidation2D.Vector(start, nameof(start));
        ModelValidation2D.Vector(end, nameof(end));
        if (start == end)
        {
            throw new ArgumentException("Two-point gradients require distinct endpoints.", nameof(end));
        }

        Vector2 difference = end - start;
        return LinearGradient(start, end, start + new Vector2(-difference.Y, difference.X), stops, extend);
    }

    /// <summary>Creates a full three-point COLRv1 linear gradient, preserving degenerate definitions.</summary>
    /// <param name="p0">The gradient origin.</param>
    /// <param name="p1">The point at gradient coordinate one.</param>
    /// <param name="p2">The direction defining the origin's perpendicular line.</param>
    /// <param name="stops">Nondecreasing finite color stops, copied in order.</param>
    /// <param name="extend">The extension rule.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D LinearGradient(Vector2 p0, Vector2 p1, Vector2 p2, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend)
    {
        ModelValidation2D.Vector(p0, nameof(p0));
        ModelValidation2D.Vector(p1, nameof(p1));
        ModelValidation2D.Vector(p2, nameof(p2));
        ModelValidation2D.EnumValue(extend, nameof(extend));
        return new PaintGraph2D(new LinearPaintNode2D(p0, p1, p2, SnapshotStops(stops), extend));
    }

    /// <summary>Creates a full two-circle COLRv1 radial gradient.</summary>
    /// <param name="c0">The finite first center.</param>
    /// <param name="r0">The finite nonnegative first radius.</param>
    /// <param name="c1">The finite second center.</param>
    /// <param name="r1">The finite nonnegative second radius.</param>
    /// <param name="stops">Nondecreasing finite color stops, copied in order.</param>
    /// <param name="extend">The extension rule.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D RadialGradient(Vector2 c0, float r0, Vector2 c1, float r1, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend)
    {
        ModelValidation2D.Vector(c0, nameof(c0));
        ModelValidation2D.Vector(c1, nameof(c1));
        ModelValidation2D.Nonnegative(r0, nameof(r0));
        ModelValidation2D.Nonnegative(r1, nameof(r1));
        ModelValidation2D.EnumValue(extend, nameof(extend));
        return new PaintGraph2D(new RadialPaintNode2D(c0, r0, c1, r1, SnapshotStops(stops), extend));
    }

    /// <summary>Creates a concentric radial gradient with a zero-radius inner circle.</summary>
    /// <param name="center">The finite center.</param>
    /// <param name="radius">The finite positive outer radius.</param>
    /// <param name="stops">Nondecreasing finite color stops, copied in order.</param>
    /// <param name="extend">The extension rule.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D RadialGradient(Vector2 center, float radius, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend = ExtendMode2D.Pad)
    {
        ModelValidation2D.Positive(radius, nameof(radius));
        return RadialGradient(center, 0, center, radius, stops, extend);
    }

    /// <summary>Creates a sweep gradient in radians, clockwise from positive X in Y-down coordinates.</summary>
    /// <param name="center">The finite center.</param>
    /// <param name="startAngle">The finite start angle.</param>
    /// <param name="endAngle">The finite end angle.</param>
    /// <param name="stops">Nondecreasing finite color stops, copied in order.</param>
    /// <param name="extend">The extension rule.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D SweepGradient(Vector2 center, float startAngle, float endAngle, ReadOnlySpan<ColorStop2D> stops, ExtendMode2D extend)
    {
        ModelValidation2D.Vector(center, nameof(center));
        ModelValidation2D.Finite(startAngle, nameof(startAngle));
        ModelValidation2D.Finite(endAngle, nameof(endAngle));
        ModelValidation2D.EnumValue(extend, nameof(extend));
        return new PaintGraph2D(new SweepPaintNode2D(center, startAngle, endAngle, SnapshotStops(stops), extend));
    }

    /// <summary>Creates a source-relative image pattern after validating and snapshotting its settings.</summary>
    /// <param name="pattern">The borrowed view and immutable sampling settings.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D ImagePattern(ImagePattern2D pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ModelValidation2D.ColorView(pattern.View, nameof(pattern));
        ModelValidation2D.Source(pattern.Source, pattern.View, nameof(pattern), integer: true);
        ModelValidation2D.EnumValue(pattern.Alpha, nameof(pattern));
        ModelValidation2D.EnumValue(pattern.TileX, nameof(pattern));
        ModelValidation2D.EnumValue(pattern.TileY, nameof(pattern));
        ModelValidation2D.EnumValue(pattern.Filter, nameof(pattern));
        ModelValidation2D.Matrix(pattern.PatternToLocal, nameof(pattern));
        if (!Matrix3x2.Invert(pattern.PatternToLocal, out Matrix3x2 inverse))
        {
            throw new ArgumentException("The pattern transform must be invertible.", nameof(pattern));
        }

        ModelValidation2D.Matrix(inverse, nameof(pattern));
        return new PaintGraph2D(new ImagePaintNode2D(pattern with { }));
    }

    /// <summary>Multiplies the completed child graph's premultiplied RGBA once.</summary>
    /// <param name="opacity">The finite multiplier between zero and one.</param>
    /// <param name="child">The immutable child graph.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D Opacity(float opacity, PaintGraph2D child)
    {
        ModelValidation2D.Unit(opacity, nameof(opacity));
        ArgumentNullException.ThrowIfNull(child);
        return Unary(new OpacityPaintNode2D(opacity, child.Root), child);
    }

    /// <summary>Clips paint to an immutable path in the same local coordinate space.</summary>
    /// <param name="path">The immutable clipping path.</param>
    /// <param name="paint">The immutable child graph.</param>
    /// <param name="rule">The path fill rule.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D Clip(Path2D path, PaintGraph2D paint, FillRule2D rule = FillRule2D.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(paint);
        ModelValidation2D.EnumValue(rule, nameof(rule));
        return Unary(new ClipPaintNode2D(path, rule, paint.Root), paint);
    }

    /// <summary>Transforms the complete child graph including its geometric clips.</summary>
    /// <param name="transform">The finite affine transform; singular transforms retain their geometry semantics.</param>
    /// <param name="child">The immutable child graph.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D Transform(Matrix3x2 transform, PaintGraph2D child)
    {
        ModelValidation2D.Matrix(transform, nameof(transform));
        ArgumentNullException.ThrowIfNull(child);
        return Unary(new TransformPaintNode2D(transform, child.Root), child);
    }

    /// <summary>Composites copied child references in order using source-over; an empty list is transparent.</summary>
    /// <param name="children">The immutable child graphs in painter order.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D Layers(ReadOnlySpan<PaintGraph2D> children)
    {
        var nodes = new PaintNode2D[children.Length];
        int depth = 1;
        int expanded = 1;
        for (int i = 0; i < children.Length; i++)
        {
            PaintGraph2D child = children[i];
            ArgumentNullException.ThrowIfNull(child, nameof(children));
            depth = Math.Max(depth, child._depth + 1);
            expanded = checked(expanded + child._expandedNodes);
            if (expanded > MaximumExpandedNodes)
            {
                throw new ArgumentException("The paint graph exceeds the expanded evaluation budget.", nameof(children));
            }

            nodes[i] = child.Root;
        }

        return new PaintGraph2D(new LayersPaintNode2D(Array.AsReadOnly(nodes)), depth, expanded);
    }

    /// <summary>Composites two independently evaluated transparent surfaces.</summary>
    /// <param name="source">The source child graph.</param>
    /// <param name="backdrop">The isolated backdrop graph, independent of Canvas contents.</param>
    /// <param name="mode">The COLRv1 compositing or blending mode.</param>
    /// <returns>The immutable paint graph.</returns>
    public static PaintGraph2D Composite(PaintGraph2D source, PaintGraph2D backdrop, CompositeMode2D mode)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(backdrop);
        ModelValidation2D.EnumValue(mode, nameof(mode));
        return new PaintGraph2D(new CompositePaintNode2D(source.Root, backdrop.Root, mode), Math.Max(source._depth, backdrop._depth) + 1, source._expandedNodes + backdrop._expandedNodes + 1);
    }

    private static PaintGraph2D Unary(PaintNode2D root, PaintGraph2D child) => new(root, child._depth + 1, child._expandedNodes + 1);

    private static IReadOnlyList<ColorStop2D> SnapshotStops(ReadOnlySpan<ColorStop2D> stops)
    {
        if (stops.IsEmpty)
        {
            throw new ArgumentException("A gradient requires at least one color stop.", nameof(stops));
        }

        float previous = float.NegativeInfinity;
        foreach (ColorStop2D stop in stops)
        {
            ModelValidation2D.Finite(stop.Offset, nameof(stops));
            ModelValidation2D.Color(stop.Color, nameof(stops));
            if (stop.Offset < previous)
            {
                throw new ArgumentException("Color stops must be in nondecreasing offset order.", nameof(stops));
            }

            previous = stop.Offset;
        }

        return Array.AsReadOnly(stops.ToArray());
    }
}
