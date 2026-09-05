using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>Records resolved 2D drawing commands without allocating per command.</summary>
public sealed class CommandEncoder : IDisposable
{
    private readonly Renderer renderer;
    private readonly List<RecordedCommand> commands = [];
    private readonly List<RecordedLayer> layers = [];
    private readonly Stack<ActiveLayer> activeLayers = [];
    private readonly Stack<State> states = [];
    private RecordedClipStack? activeClips;
    private State state = new(Matrix3x2.Identity, null, false);
    private int nextSequence;
    private bool finished;

    internal CommandEncoder(Renderer renderer) => this.renderer = renderer;

    internal Renderer Owner => renderer;

    public int Count => commands.Count;
    /// <summary>The number of scoped clips that still require a matching <see cref="PopClip"/>.</summary>
    public int ClipDepth => activeClips?.Depth ?? 0;

    /// <summary>Begins an isolated compositing group. Calls may be nested.</summary>
    public void PushLayer() => PushLayer(new LayerOptions());

    /// <summary>Begins an isolated compositing group with explicit options. Calls may be nested.</summary>
    public void PushLayer(LayerOptions options)
    {
        VerifyOpen();
        options = options.Validate(renderer, nameof(options));
        int id = checked(layers.Count + 1);
        int parentId = activeLayers.TryPeek(out ActiveLayer parent) ? parent.Id : 0;
        RecordedClipStack? layerClips = ClipsAbove(
            activeClips,
            parentId == 0 ? null : parent.ClipBoundary);
        layers.Add(new(
            id,
            parentId,
            TakeSequence(),
            options,
            state.Clip,
            layerClips,
            state.ClippedOut || layerClips is { Bounds: null }));
        activeLayers.Push(new(id, activeClips));
    }

    /// <summary>Ends the innermost isolated compositing group.</summary>
    public void PopLayer()
    {
        VerifyOpen();
        if (!activeLayers.TryPop(out _))
        {
            throw new InvalidOperationException("There is no active 2D layer to pop.");
        }
    }

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

    /// <summary>Pushes an exact transformed rectangle clip until <see cref="PopClip"/> is called.</summary>
    public void PushClip(Rect rectangle)
    {
        VerifyOpen();
        rectangle.Validate();
        activeClips = new(
            activeClips,
            new(RecordedClipKind.Rectangle, rectangle, null, state.Transform, FillRule.NonZero));
    }

    /// <summary>Pushes a path clip in the current coordinate system.</summary>
    public void PushClip(PathGeometry path, FillRule fillRule = FillRule.NonZero)
        => PushClip(path, Matrix3x2.Identity, fillRule);

    /// <summary>Pushes a transformed path clip until <see cref="PopClip"/> is called.</summary>
    public void PushClip(PathGeometry path, Matrix3x2 transform, FillRule fillRule = FillRule.NonZero)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsEmpty) { throw new ArgumentException("Clip path cannot be empty.", nameof(path)); }
        ValidateTransform(transform);
        if (!Enum.IsDefined(fillRule)) { throw new ArgumentOutOfRangeException(nameof(fillRule)); }
        activeClips = new(
            activeClips,
            new(
                RecordedClipKind.Path,
                default,
                path,
                transform * state.Transform,
                fillRule));
    }

    /// <summary>Removes the most recently pushed rectangle or path clip.</summary>
    public void PopClip()
    {
        VerifyOpen();
        if (activeClips is null)
        {
            throw new InvalidOperationException("There is no active 2D clip to pop.");
        }
        activeClips = activeClips.Parent;
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

    /// <summary>Adds a caller-selected coverage or SDF atlas route.</summary>
    public void DrawDistanceField(DistanceField field, Rect destination, Brush brush)
    {
        ImageId image = renderer.RequireDistanceField(field);
        DistanceFieldEntry entry = field.Owner!.Require(field);
        GpuTextureDescription atlas = field.Owner.Description;
        Rect source = new(
            (float)entry.Region.X / atlas.Width,
            (float)entry.Region.Y / atlas.Height,
            (float)entry.Region.Width / atlas.Width,
            (float)entry.Region.Height / atlas.Height);
        Add(new(
            DrawCommandKind.DistanceField,
            destination.Validate(),
            brush.Validate(),
            state.Transform,
            state.Clip,
            Image: image,
            Source: source,
            DistanceField: field));
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

    public void DrawPath(
        PathGeometry path,
        Matrix3x2 transform,
        Brush brush,
        FillRule fillRule = FillRule.NonZero,
        PathClip? clip = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsEmpty) { throw new ArgumentException("Path cannot be empty.", nameof(path)); }
        ValidateTransform(transform);
        if (!Enum.IsDefined(fillRule)) { throw new ArgumentOutOfRangeException(nameof(fillRule)); }
        Add(new(
            DrawCommandKind.Path,
            path.Bounds,
            brush.Validate(),
            transform * state.Transform,
            state.Clip,
            Path: path,
            FillRule: fillRule,
            PathClip: clip?.Validate()));
    }

    public void StrokePath(
        PathGeometry path,
        Matrix3x2 transform,
        StrokeStyle stroke,
        Brush brush,
        PathClip? clip = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(stroke);
        if (path.IsEmpty) { throw new ArgumentException("Path cannot be empty.", nameof(path)); }
        ValidateTransform(transform);
        float halfWidth = stroke.Width * 0.5f;
        Rect bounds = path.Bounds;
        bounds = new(
            bounds.X - halfWidth,
            bounds.Y - halfWidth,
            bounds.Width + stroke.Width,
            bounds.Height + stroke.Width);
        Add(new(
            DrawCommandKind.Path,
            bounds,
            brush.Validate(),
            transform * state.Transform,
            state.Clip,
            Path: path,
            Stroke: stroke,
            PathClip: clip?.Validate()));
    }

    public DisplayList Finish()
    {
        VerifyOpen();
        if (states.Count != 0)
        {
            throw new InvalidOperationException("Every saved 2D state must be restored before finishing.");
        }
        if (activeLayers.Count != 0)
        {
            throw new InvalidOperationException("Every pushed 2D layer must be popped before finishing.");
        }
        if (activeClips is not null)
        {
            throw new InvalidOperationException("Every pushed 2D clip must be popped before finishing.");
        }
        finished = true;
        return new(renderer, commands.ToArray(), layers.ToArray());
    }

    public void Dispose() => finished = true;

    private void AddShape(DrawCommandKind kind, Rect bounds, Brush brush)
        => Add(new(kind, bounds.Validate(), brush.Validate(), state.Transform, state.Clip));

    private void Add(RecordedCommand command)
    {
        VerifyOpen();
        if (!state.ClippedOut)
        {
            int layerId = 0;
            RecordedClipStack? commandClips = activeClips;
            if (activeLayers.TryPeek(out ActiveLayer active))
            {
                layerId = active.Id;
                commandClips = ClipsAbove(activeClips, active.ClipBoundary);
            }
            if (commandClips is { Bounds: null })
            {
                return;
            }
            Rect? clip = command.Clip;
            if (commandClips?.Bounds is { } scopedBounds)
            {
                clip = clip is { } stateBounds
                    ? Rect.Intersect(stateBounds, scopedBounds)
                    : scopedBounds;
                if (clip is null)
                {
                    return;
                }
            }
            commands.Add(command with
            {
                LayerId = layerId,
                Clip = clip,
                ClipStack = commandClips,
                Sequence = TakeSequence(),
            });
        }
    }

    private static RecordedClipStack? ClipsAbove(
        RecordedClipStack? current,
        RecordedClipStack? boundary)
    {
        if (boundary is null) { return current; }

        var boundaryAncestors = new HashSet<RecordedClipStack>();
        for (RecordedClipStack? item = boundary; item is not null; item = item.Parent)
        {
            boundaryAncestors.Add(item);
        }

        var additions = new Stack<RecordedClip>();
        RecordedClipStack? cursor = current;
        while (cursor is not null && !boundaryAncestors.Contains(cursor))
        {
            additions.Push(cursor.Clip);
            cursor = cursor.Parent;
        }

        RecordedClipStack? result = null;
        while (additions.TryPop(out RecordedClip clip))
        {
            result = new(result, clip);
        }
        return result;
    }

    private int TakeSequence()
    {
        int sequence = nextSequence;
        nextSequence = checked(nextSequence + 1);
        return sequence;
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

    private readonly record struct ActiveLayer(int Id, RecordedClipStack? ClipBoundary);
}
