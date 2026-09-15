using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public abstract class Brush
{
    private protected Brush() { }
    public static SolidBrush Solid(Color color) => new(color);
    public static LinearGradientBrush LinearGradient(Vector2 start, Vector2 end, Color startColor, Color endColor) =>
        new(start, end, [new(0, startColor), new(1, endColor)]);
    public static LinearGradientBrush LinearGradient(Vector2 start, Vector2 end, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) => new(start, end, stops, extendMode);
    public static LinearGradientBrush LinearGradient(Vector2 point0, Vector2 point1, Vector2 point2, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) => new(point0, point1, stops, extendMode, point2);
    public static RadialGradientBrush RadialGradient(Vector2 center, float radius, Color innerColor, Color outerColor) => new(center, 0, center, radius, [new(0, innerColor), new(1, outerColor)]);
    public static RadialGradientBrush RadialGradient(Vector2 center0, float radius0, Vector2 center1, float radius1, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) => new(center0, radius0, center1, radius1, stops, extendMode);
    public static SweepGradientBrush SweepGradient(Vector2 center, float startAngle, float endAngle, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) => new(center, startAngle, endAngle, stops, extendMode);
    public static ImageBrush Image(Draw2DImageSource source, Matrix3x2 transform, Draw2DImageSampling sampling = default) => new(source, transform, sampling);
    internal static void Point(Vector2 point, string name)
    { if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) { throw new ArgumentOutOfRangeException(name); } }
}

public sealed class SolidBrush : Brush
{
    public SolidBrush(Color color) => Color = color.Validate();
    public Color Color { get; }
}

public abstract class GradientBrush : Brush
{
    private protected GradientBrush(IEnumerable<GradientStop> stops, GradientExtendMode extendMode)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var copy = stops.OrderBy(s => s.Offset).ToArray();
        if (copy.Length == 0)
        { throw new ArgumentException("A gradient requires a color stop.", nameof(stops)); }
        foreach (var stop in copy)
        { stop.Validate(nameof(stops)); }
        if (!Enum.IsDefined(extendMode))
        { throw new ArgumentOutOfRangeException(nameof(extendMode)); }
        Stops = Array.AsReadOnly(copy);
        ExtendMode = extendMode;
    }
    public IReadOnlyList<GradientStop> Stops { get; }
    public GradientExtendMode ExtendMode { get; }
}

public sealed class LinearGradientBrush : GradientBrush
{
    public LinearGradientBrush(Vector2 start, Vector2 end, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad, Vector2? projection = null) : base(stops, extendMode)
    {
        Point(start, nameof(start));
        Point(end, nameof(end));
        if (start == end)
        { throw new ArgumentException("Gradient anchors must differ.", nameof(end)); }
        Start = start;
        End = end;
        Projection = projection ?? start + new Vector2(-(end.Y - start.Y), end.X - start.X);
        Point(Projection, nameof(projection));
    }
    public Vector2 Start { get; }
    public Vector2 End { get; }
    public Vector2 Projection { get; }
}

public sealed class RadialGradientBrush : GradientBrush
{
    public RadialGradientBrush(Vector2 center0, float radius0, Vector2 center1, float radius1, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) : base(stops, extendMode)
    {
        Point(center0, nameof(center0));
        Point(center1, nameof(center1));
        if (!float.IsFinite(radius0))
        { throw new ArgumentOutOfRangeException(nameof(radius0)); }
        if (!float.IsFinite(radius1))
        { throw new ArgumentOutOfRangeException(nameof(radius1)); }
        Center0 = center0;
        Center1 = center1;
        Radius0 = radius0;
        Radius1 = radius1;
    }
    public Vector2 Center0 { get; }
    public Vector2 Center1 { get; }
    public float Radius0 { get; }
    public float Radius1 { get; }
}

public sealed class SweepGradientBrush : GradientBrush
{
    public SweepGradientBrush(Vector2 center, float startAngle, float endAngle, IEnumerable<GradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) : base(stops, extendMode)
    {
        Point(center, nameof(center));
        if (!float.IsFinite(startAngle))
        { throw new ArgumentOutOfRangeException(nameof(startAngle)); }
        if (!float.IsFinite(endAngle) || startAngle == endAngle)
        { throw new ArgumentOutOfRangeException(nameof(endAngle)); }
        Center = center;
        StartAngle = startAngle;
        EndAngle = endAngle;
    }
    public Vector2 Center { get; }
    public float StartAngle { get; }
    public float EndAngle { get; }
}

public sealed class ImageBrush : Brush
{
    public ImageBrush(Draw2DImageSource source, Matrix3x2 transform, Draw2DImageSampling sampling = default)
    { Source = source ?? throw new ArgumentNullException(nameof(source)); Transform = transform; Sampling = sampling; }
    public Draw2DImageSource Source { get; }
    public Matrix3x2 Transform { get; }
    public Draw2DImageSampling Sampling { get; }
}
