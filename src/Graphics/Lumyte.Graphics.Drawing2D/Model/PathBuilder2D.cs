using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Builds immutable paths without allocating GPU resources.</summary>
public sealed class PathBuilder2D
{
    private readonly List<PathSegment2D> _segments = [];
    private bool _hasCurrentPoint;

    /// <summary>Initializes a new instance of the <see cref="PathBuilder2D"/> class.</summary>
    public PathBuilder2D()
    {
    }

    /// <summary>Starts a contour at a finite logical point.</summary>
    /// <param name="point">The new contour start.</param>
    public void MoveTo(Vector2 point)
    {
        ModelValidation2D.Vector(point, nameof(point));
        _segments.Add(new PathSegment2D(PathVerb2D.Move, point, default, default));
        _hasCurrentPoint = true;
    }

    /// <summary>Adds a line from the current point.</summary>
    /// <param name="point">The finite endpoint.</param>
    public void LineTo(Vector2 point)
    {
        RequireCurrentPoint();
        ModelValidation2D.Vector(point, nameof(point));
        _segments.Add(new PathSegment2D(PathVerb2D.Line, point, default, default));
    }

    /// <summary>Adds a quadratic curve from the current point.</summary>
    /// <param name="control">The finite control point.</param>
    /// <param name="end">The finite endpoint.</param>
    public void QuadraticTo(Vector2 control, Vector2 end)
    {
        RequireCurrentPoint();
        ModelValidation2D.Vector(control, nameof(control));
        ModelValidation2D.Vector(end, nameof(end));
        _segments.Add(new PathSegment2D(PathVerb2D.Quadratic, control, end, default));
    }

    /// <summary>Adds a cubic curve from the current point.</summary>
    /// <param name="control1">The first finite control point.</param>
    /// <param name="control2">The second finite control point.</param>
    /// <param name="end">The finite endpoint.</param>
    public void CubicTo(Vector2 control1, Vector2 control2, Vector2 end)
    {
        RequireCurrentPoint();
        ModelValidation2D.Vector(control1, nameof(control1));
        ModelValidation2D.Vector(control2, nameof(control2));
        ModelValidation2D.Vector(end, nameof(end));
        _segments.Add(new PathSegment2D(PathVerb2D.Cubic, control1, control2, end));
    }

    /// <summary>Closes the current contour and returns its current point to the contour start.</summary>
    public void Close()
    {
        RequireCurrentPoint();
        _segments.Add(new PathSegment2D(PathVerb2D.Close, default, default, default));
    }

    /// <summary>Copies the current segments into an independent immutable path.</summary>
    /// <returns>The immutable path.</returns>
    public Path2D Build() => new(_segments);

    private void RequireCurrentPoint()
    {
        if (!_hasCurrentPoint)
        {
            throw new InvalidOperationException("MoveTo must establish a current point first.");
        }
    }
}
