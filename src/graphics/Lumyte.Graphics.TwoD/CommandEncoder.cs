using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>Records resolved 2D drawing commands without allocating per command.</summary>
public sealed class CommandEncoder : IDisposable
{
    private readonly Renderer renderer;
    private readonly List<RecordedCommand> commands = [];
    private readonly Stack<State> states = [];
    private State state = new(Matrix3x2.Identity, null, false);
    private bool finished;

    internal CommandEncoder(Renderer renderer) => this.renderer = renderer;

    public int Count => commands.Count;

    public void Save()
    {
        VerifyOpen();
        states.Push(state);
    }

    public void Restore()
    {
        VerifyOpen();
        if (!states.TryPop(out state))
        {
            throw new InvalidOperationException("There is no saved 2D state to restore.");
        }
    }

    public void SetTransform(Matrix3x2 transform)
    {
        VerifyOpen();
        ValidateTransform(transform);
        state = state with { Transform = transform };
    }

    public void Transform(Matrix3x2 transform)
    {
        VerifyOpen();
        ValidateTransform(transform);
        state = state with { Transform = state.Transform * transform };
    }

    /// <summary>Intersects the current target-space clip with a transformed local rectangle.</summary>
    public void Clip(Rect rectangle)
    {
        VerifyOpen();
        rectangle.Validate();
        if (state.ClippedOut) { return; }
        Rect transformed = rectangle.TransformBounds(state.Transform);
        Rect? clip = state.Clip is { } current ? Rect.Intersect(current, transformed) : transformed;
        state = clip is null
            ? state with { Clip = null, ClippedOut = true }
            : state with { Clip = clip };
    }

    public void FillRectangle(Rect rectangle, Brush brush)
        => AddShape(DrawCommandKind.Rectangle, rectangle, brush);

    public void FillRoundedRectangle(Rect rectangle, CornerRadius radius, Brush brush)
    {
        radius.Validate();
        Add(new(
            DrawCommandKind.RoundedRectangle,
            rectangle.Validate(),
            brush.Validate(),
            state.Transform,
            state.Clip,
            CornerRadius: radius.Clamp(rectangle)));
    }

    public void FillEllipse(Rect bounds, Brush brush)
        => AddShape(DrawCommandKind.Ellipse, bounds, brush);

    public void DrawLine(Vector2 start, Vector2 end, float width, Brush brush)
    {
        VerifyFinite(start, nameof(start));
        VerifyFinite(end, nameof(end));
        if (!float.IsFinite(width) || width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        float halfWidth = width * 0.5f;
        float left = MathF.Min(start.X, end.X) - halfWidth;
        float top = MathF.Min(start.Y, end.Y) - halfWidth;
        var bounds = new Rect(
            left,
            top,
            MathF.Max(MathF.Abs(end.X - start.X) + width, width),
            MathF.Max(MathF.Abs(end.Y - start.Y) + width, width));
        Add(new(
            DrawCommandKind.Line,
            bounds,
            brush.Validate(),
            state.Transform,
            state.Clip,
            LineStart: start,
            LineEnd: end,
            LineWidth: width));
    }

    public void DrawImage(
        ImageId image,
        Rect destination,
        Color? tint = null,
        Rect? source = null)
    {
        renderer.RequireImage(image);
        Rect sourceRectangle = (source ?? new(0, 0, 1, 1)).Validate();
        Add(new(
            DrawCommandKind.Image,
            destination.Validate(),
            Brush.Solid(tint ?? Color.White),
            state.Transform,
            state.Clip,
            Image: image,
            Source: sourceRectangle));
    }

    public void DrawGeometry(PolygonGeometry geometry, Matrix3x2 transform, Brush brush)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ValidateTransform(transform);
        Add(new(
            DrawCommandKind.Polygon,
            Bounds(geometry.Vertices.Span),
            brush.Validate(),
            transform * state.Transform,
            state.Clip,
            Geometry: geometry));
    }

    public DisplayList Finish()
    {
        VerifyOpen();
        if (states.Count != 0)
        {
            throw new InvalidOperationException("Every saved 2D state must be restored before finishing.");
        }
        finished = true;
        return new(renderer, commands.ToArray());
    }

    public void Dispose() => finished = true;

    private void AddShape(DrawCommandKind kind, Rect bounds, Brush brush)
        => Add(new(kind, bounds.Validate(), brush.Validate(), state.Transform, state.Clip));

    private void Add(RecordedCommand command)
    {
        VerifyOpen();
        if (!state.ClippedOut) { commands.Add(command); }
    }

    private void VerifyOpen()
        => ObjectDisposedException.ThrowIf(finished, this);

    private static void ValidateTransform(Matrix3x2 transform)
    {
        if (!float.IsFinite(transform.M11) || !float.IsFinite(transform.M12)
            || !float.IsFinite(transform.M21) || !float.IsFinite(transform.M22)
            || !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32))
        {
            throw new ArgumentOutOfRangeException(nameof(transform));
        }
    }

    private static void VerifyFinite(Vector2 value, string parameter)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }

    private static Rect Bounds(ReadOnlySpan<Vector2> vertices)
    {
        float left = float.PositiveInfinity;
        float top = float.PositiveInfinity;
        float right = float.NegativeInfinity;
        float bottom = float.NegativeInfinity;
        foreach (Vector2 vertex in vertices)
        {
            left = MathF.Min(left, vertex.X);
            top = MathF.Min(top, vertex.Y);
            right = MathF.Max(right, vertex.X);
            bottom = MathF.Max(bottom, vertex.Y);
        }
        return new Rect(left, top, right - left, bottom - top).Validate();
    }

    private readonly record struct State(Matrix3x2 Transform, Rect? Clip, bool ClippedOut);
}
