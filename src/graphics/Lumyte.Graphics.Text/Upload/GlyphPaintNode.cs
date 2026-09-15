using System.Numerics;

using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Resolved immutable color glyph paint. No font lookup is performed during drawing.</summary>
public abstract class GlyphPaintNode
{
    private protected GlyphPaintNode(IEnumerable<GpuImageUploadData>? images = null)
        => Images = Array.AsReadOnly(images?.Distinct().ToArray() ?? []);
    public IReadOnlyList<GpuImageUploadData> Images { get; }

    public static GlyphPaintNode LinearGradient(Vector2 start, Vector2 end, IEnumerable<GlyphGradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) =>
        GradientPaint(stops, colors => Brush.LinearGradient(start, end, colors, extendMode));
    public static GlyphPaintNode LinearGradient(Vector2 point0, Vector2 point1, Vector2 point2, IEnumerable<GlyphGradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) =>
        GradientPaint(stops, colors => Brush.LinearGradient(point0, point1, point2, colors, extendMode));
    public static GlyphPaintNode RadialGradient(Vector2 center0, float radius0, Vector2 center1, float radius1, IEnumerable<GlyphGradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) =>
        GradientPaint(stops, colors => Brush.RadialGradient(center0, radius0, center1, radius1, colors, extendMode));
    public static GlyphPaintNode SweepGradient(Vector2 center, float startAngle, float endAngle, IEnumerable<GlyphGradientStop> stops, GradientExtendMode extendMode = GradientExtendMode.Pad) =>
        GradientPaint(stops, colors => Brush.SweepGradient(center, startAngle, endAngle, colors, extendMode));

    private static GlyphPaintNode GradientPaint(IEnumerable<GlyphGradientStop> source, Func<GradientStop[], GradientBrush> create)
    {
        ArgumentNullException.ThrowIfNull(source);
        var stops = source.ToArray();
        var resolved = new Gradient(create(stops.Select(stop => new GradientStop(stop.Offset, stop.IsForeground ? Color.Transparent : stop.Color)).ToArray()));
        if (!stops.Any(stop => stop.IsForeground))
        { return resolved; }
        // Premultiplied interpolation distributes over the resolved color contribution and
        // the foreground contribution. Keep that composition semantic and backend independent.
        var weights = new Gradient(create(stops.Select(stop => new GradientStop(stop.Offset,
            stop.IsForeground ? new Color(1, 1, 1, stop.ForegroundAlpha) : Color.Transparent)).ToArray()));
        var foreground = new Composite(weights, new Foreground(), CompositeMode.DestinationIn);
        return new Composite(foreground, resolved, CompositeMode.Plus);
    }

    public sealed class Outline : GlyphPaintNode
    {
        public Outline(PathGeometry path, FillRule fillRule, GlyphPaintNode paint) : base(paint.Images) { Path = path; FillRule = fillRule; Paint = paint; }
        public PathGeometry Path { get; }
        public FillRule FillRule { get; }
        public GlyphPaintNode Paint { get; }
    }
    public sealed class Solid(Color color) : GlyphPaintNode { public Color Color { get; } = color.Validate(); }
    public sealed class Foreground : GlyphPaintNode
    {
        public Foreground(float alpha = 1) { if (!float.IsFinite(alpha) || alpha is < 0 or > 1) { throw new ArgumentOutOfRangeException(nameof(alpha)); } Alpha = alpha; }
        public float Alpha { get; }
    }
    public sealed class Gradient(GradientBrush brush) : GlyphPaintNode { public GradientBrush Brush { get; } = brush; }
    public sealed class Bitmap : GlyphPaintNode
    {
        public Bitmap(GpuImageUploadData image, Rect sourceRectangle, Rect destination) : base([image]) { Image = image; SourceRectangle = sourceRectangle; Destination = destination; }
        public GpuImageUploadData Image { get; }
        public Rect SourceRectangle { get; }
        public Rect Destination { get; }
    }
    public sealed class Transform : GlyphPaintNode
    {
        public Transform(Matrix3x2 matrix, GlyphPaintNode child) : base(child.Images) { Matrix = matrix; Child = child; }
        public Matrix3x2 Matrix { get; }
        public GlyphPaintNode Child { get; }
    }
    public sealed class Clip : GlyphPaintNode
    {
        public Clip(PathGeometry path, FillRule fillRule, GlyphPaintNode child) : base(child.Images) { Path = path; FillRule = fillRule; Child = child; }
        public PathGeometry Path { get; }
        public FillRule FillRule { get; }
        public GlyphPaintNode Child { get; }
    }
    public sealed class Composite : GlyphPaintNode
    {
        public Composite(GlyphPaintNode source, GlyphPaintNode backdrop, CompositeMode mode) : base(source.Images.Concat(backdrop.Images)) { Source = source; Backdrop = backdrop; Mode = mode; }
        public GlyphPaintNode Source { get; }
        public GlyphPaintNode Backdrop { get; }
        public CompositeMode Mode { get; }
    }
    public sealed class Layers : GlyphPaintNode
    {
        public Layers(IEnumerable<GlyphPaintNode> children) : this(children.ToArray()) { }
        private Layers(GlyphPaintNode[] children) : base(children.SelectMany(child => child.Images)) { Children = Array.AsReadOnly(children); }
        public IReadOnlyList<GlyphPaintNode> Children { get; }
    }
}
