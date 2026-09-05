using System.Numerics;

namespace Lumyte.Graphics.TwoD;

internal readonly record struct RecordedCommand(
    DrawCommandKind Kind,
    Rect Bounds,
    Brush Brush,
    Matrix3x2 Transform,
    Rect? Clip,
    CornerRadius CornerRadius = default,
    Vector2 LineStart = default,
    Vector2 LineEnd = default,
    float LineWidth = 0,
    ImageId Image = default,
    Rect Source = default,
    PolygonGeometry? Geometry = null,
    DistanceField DistanceField = default,
    PathGeometry? Path = null,
    FillRule FillRule = FillRule.NonZero,
    StrokeStyle? Stroke = null,
    PathClip? PathClip = null,
    int LayerId = 0,
    RecordedClipStack? ClipStack = null,
    int Sequence = 0);
