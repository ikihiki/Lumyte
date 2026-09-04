using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>Builds reusable vector paths without flattening their curves.</summary>
public sealed class PathBuilder
{
    private readonly List<PathSegment> segments = [];
    private Vector2 current;
    private Vector2 figureStart;
    private bool hasCurrent;

    public PathBuilder MoveTo(Vector2 point)
    {
        Validate(point, nameof(point));
        segments.Add(new(PathSegmentKind.Move, point));
        current = figureStart = point;
        hasCurrent = true;
        return this;
    }

    public PathBuilder LineTo(Vector2 point)
    {
        RequireFigure();
        Validate(point, nameof(point));
        segments.Add(new(PathSegmentKind.Line, point));
        current = point;
        return this;
    }

    public PathBuilder QuadraticTo(Vector2 control, Vector2 point)
    {
        RequireFigure();
        Validate(control, nameof(control));
        Validate(point, nameof(point));
        segments.Add(new(PathSegmentKind.Quadratic, point, control));
        current = point;
        return this;
    }

    public PathBuilder CubicTo(Vector2 control0, Vector2 control1, Vector2 point)
    {
        RequireFigure();
        Validate(control0, nameof(control0));
        Validate(control1, nameof(control1));
        Validate(point, nameof(point));
        segments.Add(new(PathSegmentKind.Cubic, point, control0, control1));
        current = point;
        return this;
    }

    public PathBuilder Close()
    {
        RequireFigure();
        if (current != figureStart)
        {
            segments.Add(new(PathSegmentKind.Close, figureStart));
        }
        current = figureStart;
        return this;
    }

    public PathGeometry Build()
    {
        if (segments.Count == 0)
        {
            throw new InvalidOperationException("A path requires at least one figure.");
        }
        if (!segments.Any(static segment => segment.Kind is not PathSegmentKind.Move))
        {
            throw new InvalidOperationException("A path requires at least one drawable segment.");
        }

        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;
        foreach (PathSegment segment in segments)
        {
            Include(segment.Point);
            if (segment.Kind is PathSegmentKind.Quadratic or PathSegmentKind.Cubic)
            {
                Include(segment.Control0);
            }
            if (segment.Kind == PathSegmentKind.Cubic)
            {
                Include(segment.Control1);
            }
        }
        return new(segments.ToArray(), new(left, top, right - left, bottom - top));

        void Include(Vector2 point)
        {
            left = MathF.Min(left, point.X);
            top = MathF.Min(top, point.Y);
            right = MathF.Max(right, point.X);
            bottom = MathF.Max(bottom, point.Y);
        }
    }

    private void RequireFigure()
    {
        if (!hasCurrent)
        {
            throw new InvalidOperationException("MoveTo must begin a path figure.");
        }
    }

    private static void Validate(Vector2 point, string parameter)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
