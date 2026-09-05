using System.Numerics;

namespace Lumyte.Graphics.TwoD;

internal enum RecordedClipKind
{
    Rectangle,
    Path,
}

internal readonly record struct RecordedClip(
    RecordedClipKind Kind,
    Rect Rectangle,
    PathGeometry? Path,
    Matrix3x2 Transform,
    FillRule FillRule)
{
    internal Rect Bounds => Kind == RecordedClipKind.Rectangle
        ? Rectangle.TransformBounds(Transform)
        : Path!.Bounds.TransformBounds(Transform);

    internal bool RequiresCoverage => Kind == RecordedClipKind.Path
        || Transform.M12 != 0
        || Transform.M21 != 0;
}

internal sealed class RecordedClipStack
{
    internal RecordedClipStack(RecordedClipStack? parent, RecordedClip clip)
    {
        Parent = parent;
        Clip = clip;
        Depth = checked((parent?.Depth ?? 0) + 1);
        Bounds = parent is null
            ? clip.Bounds
            : parent.Bounds is { } parentBounds
                ? Rect.Intersect(parentBounds, clip.Bounds)
                : null;
        RequiresCoverage = clip.RequiresCoverage || (parent?.RequiresCoverage ?? false);
    }

    internal RecordedClipStack? Parent { get; }
    internal RecordedClip Clip { get; }
    internal int Depth { get; }
    internal Rect? Bounds { get; }
    internal bool RequiresCoverage { get; }
}
