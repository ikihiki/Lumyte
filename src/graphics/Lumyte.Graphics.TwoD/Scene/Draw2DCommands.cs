using System.Numerics;

using Lumyte.Graphics.Text;

namespace Lumyte.Graphics.TwoD;

public enum Draw2DShapeKind { Rectangle, RoundedRectangle, Ellipse }
public sealed record Draw2DClip(Rect? Rectangle, PathGeometry? Path, FillRule FillRule, Matrix3x2 Transform);
public sealed class Draw2DState
{
    public Draw2DState(Matrix3x2 transform, IEnumerable<Draw2DClip>? clips = null)
    { Transform = transform; Clips = Array.AsReadOnly(clips?.ToArray() ?? []); }
    public static Draw2DState Identity { get; } = new(Matrix3x2.Identity);
    public Matrix3x2 Transform { get; }
    public IReadOnlyList<Draw2DClip> Clips { get; }
}

public abstract record Draw2DCommand(Draw2DState State);
public sealed record Draw2DShapeCommand(Draw2DShapeKind Kind, Rect Bounds, CornerRadius CornerRadius, Brush Brush, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DPathCommand(PathGeometry Path, Brush Brush, FillRule FillRule, StrokeStyle? Stroke, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DGeometryCommand(PolygonGeometry Geometry, Brush Brush, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DImageCommand(Draw2DImageSource Source, Rect Destination, Rect SourceRectangle, Color Tint, Draw2DImageSampling Sampling, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DDistanceFieldCommand(DistanceFieldUploadData Data, Rect Destination, Brush Brush, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DTextCommand(TextDrawData Data, Vector2 Origin, Brush Brush, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DLayerCommand(Draw2DLayerOptions Options, Draw2DScene Content, Draw2DState State) : Draw2DCommand(State);
public sealed record Draw2DSceneCommand(Draw2DScene Content, Draw2DState State) : Draw2DCommand(State);
