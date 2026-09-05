using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>Records resolved 2D drawing commands without allocating per command.</summary>
public sealed class CommandEncoder : IDisposable
{
    private readonly Renderer renderer;
    private readonly List<RecordedCommand> commands = [];
    private readonly List<RecordedLayer> layers = [];
    private readonly List<ScopeFrame> scopes = [];
    private State state = new(Matrix3x2.Identity, null, false, 0, null, null);
    private int nextSequence;
    private int nextScopeId;
    private EncoderStatus status;

    internal CommandEncoder(Renderer renderer) => this.renderer = renderer;

    internal Renderer Owner => renderer;

    public int Count => commands.Count;
    /// <summary>The number of exact clips active in the current scope.</summary>
    public int ClipDepth => state.Clips?.Depth ?? 0;

    /// <summary>Begins a state scope which restores transform, clip, and layer state when disposed.</summary>
    public CommandEncoderScope BeginState() => BeginScope(ScopeKind.State);

    /// <summary>Begins an exact rectangle clip fixed using the current transform.</summary>
    public CommandEncoderScope BeginClip(Rect rectangle)
    {
        rectangle.Validate();
        CommandEncoderScope scope = BeginScope(ScopeKind.Clip);
        state = state with
        {
            Clips = new(state.Clips, new(RecordedClipKind.Rectangle, rectangle, null, state.Transform, FillRule.NonZero)),
        };
        return scope;
    }

    /// <summary>Begins an exact path clip fixed using the current transform.</summary>
    public CommandEncoderScope BeginClip(PathGeometry path, FillRule fillRule = FillRule.NonZero)
        => BeginClip(path, Matrix3x2.Identity, fillRule);

    /// <summary>Begins an exact path clip using an additional local transform.</summary>
    public CommandEncoderScope BeginClip(PathGeometry path, Matrix3x2 transform, FillRule fillRule = FillRule.NonZero)
    {
        VerifyOpen();
        ArgumentNullException.ThrowIfNull(path);
        if (path.IsEmpty) { throw new ArgumentException("Clip path cannot be empty.", nameof(path)); }
        ValidateTransform(transform);
        if (!Enum.IsDefined(fillRule)) { throw new ArgumentOutOfRangeException(nameof(fillRule)); }
        CommandEncoderScope scope = BeginScope(ScopeKind.Clip);
        state = state with
        {
            Clips = new(state.Clips, new(RecordedClipKind.Path, default, path, transform * state.Transform, fillRule)),
        };
        return scope;
    }

    /// <summary>Begins an isolated compositing layer and restores all drawing state when disposed.</summary>
    public CommandEncoderScope BeginLayer() => BeginLayer(new LayerOptions());

    /// <summary>Begins an isolated compositing layer with explicit options.</summary>
    public CommandEncoderScope BeginLayer(LayerOptions options)
    {
        VerifyOpen();
        options = options.Validate(renderer, nameof(options));
        CommandEncoderScope scope = BeginScope(ScopeKind.Layer);
        int id = checked(layers.Count + 1);
        RecordedClipStack? layerClips = ClipsAbove(state.Clips, state.LayerClipBoundary);
        layers.Add(new(
            id,
            state.LayerId,
            TakeSequence(),
            options,
            null,
            layerClips,
            layerClips is { Bounds: null }));
        state = state with { LayerId = id, LayerClipBoundary = state.Clips };
        return scope;
    }

    /// <summary>Begins an isolated compositing group. Calls may be nested.</summary>
    public void PushLayer() => BeginLayer();

    /// <summary>Begins an isolated compositing group with explicit options. Calls may be nested.</summary>
    public void PushLayer(LayerOptions options)
    {
        VerifyOpen();
        _ = BeginLayer(options);
    }

    /// <summary>Ends the innermost isolated compositing group.</summary>
    public void PopLayer()
    {
        EndLegacyScope(ScopeKind.Layer, "There is no active 2D layer to pop.");
    }

    public void Save() => _ = BeginState();

    public void Restore()
    {
        EndLegacyScope(ScopeKind.State, "There is no saved 2D state to restore.");
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
        _ = BeginClip(rectangle);
    }

    /// <summary>Pushes a path clip in the current coordinate system.</summary>
    public void PushClip(PathGeometry path, FillRule fillRule = FillRule.NonZero)
        => PushClip(path, Matrix3x2.Identity, fillRule);

    /// <summary>Pushes a transformed path clip until <see cref="PopClip"/> is called.</summary>
    public void PushClip(PathGeometry path, Matrix3x2 transform, FillRule fillRule = FillRule.NonZero)
    {
        _ = BeginClip(path, transform, fillRule);
    }

    /// <summary>Removes the most recently pushed rectangle or path clip.</summary>
    public void PopClip()
    {
        EndLegacyScope(ScopeKind.Clip, "There is no active 2D clip to pop.");
    }

    /// <summary>Intersects the current target-space clip with a transformed local rectangle.</summary>
    public void Clip(Rect rectangle)
    {
        VerifyOpen();
        rectangle.Validate();
        state = state with
        {
            Clips = new(state.Clips, new(RecordedClipKind.Rectangle, rectangle, null, state.Transform, FillRule.NonZero)),
        };
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
        if (scopes.Count != 0)
        {
            string requirement = scopes[^1].Kind switch
            {
                ScopeKind.State => "Every saved 2D state must be restored before finishing.",
                ScopeKind.Clip => "Every 2D clip scope must be disposed before finishing.",
                ScopeKind.Layer => "Every pushed 2D layer must be popped before finishing.",
                _ => "Every 2D scope must be disposed before finishing.",
            };
            throw new InvalidOperationException(requirement);
        }
        status = EncoderStatus.Finished;
        return new(renderer, commands.ToArray(), layers.ToArray());
    }

    public void Dispose()
    {
        if (status == EncoderStatus.Disposed) { return; }
        if (status == EncoderStatus.Recording)
        {
            commands.Clear();
            layers.Clear();
            scopes.Clear();
        }
        status = EncoderStatus.Disposed;
    }

    private void AddShape(DrawCommandKind kind, Rect bounds, Brush brush)
        => Add(new(kind, bounds.Validate(), brush.Validate(), state.Transform, state.Clip));

    private void Add(RecordedCommand command)
    {
        VerifyOpen();
        if (!state.ClippedOut)
        {
            int layerId = state.LayerId;
            RecordedClipStack? commandClips = ClipsAbove(state.Clips, state.LayerClipBoundary);
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

    internal void EndScope(int scopeId)
    {
        VerifyOpen();
        int index = scopes.Count - 1;
        if (index < 0 || scopes[index].Id != scopeId)
        {
            if (!scopes.Exists(frame => frame.Id == scopeId)) { return; }
            throw new InvalidOperationException("2D scopes must be disposed in reverse order.");
        }
        state = scopes[index].SavedState;
        scopes.RemoveAt(index);
    }

    private CommandEncoderScope BeginScope(ScopeKind kind)
    {
        VerifyOpen();
        int id = checked(++nextScopeId);
        scopes.Add(new(id, kind, state));
        return new(this, id);
    }

    private void EndLegacyScope(ScopeKind kind, string emptyMessage)
    {
        VerifyOpen();
        if (scopes.Count == 0) { throw new InvalidOperationException(emptyMessage); }
        ScopeFrame frame = scopes[^1];
        if (frame.Kind != kind)
        {
            throw new InvalidOperationException("2D scopes must be disposed in reverse order.");
        }
        EndScope(frame.Id);
    }

    private void VerifyOpen()
    {
        if (status == EncoderStatus.Recording) { return; }
        if (status == EncoderStatus.Finished)
        {
            throw new InvalidOperationException("The 2D command encoder has already been finished.");
        }
        throw new ObjectDisposedException(nameof(CommandEncoder));
    }

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

    private readonly record struct State(
        Matrix3x2 Transform,
        Rect? Clip,
        bool ClippedOut,
        int LayerId,
        RecordedClipStack? LayerClipBoundary,
        RecordedClipStack? Clips);

    private readonly record struct ScopeFrame(int Id, ScopeKind Kind, State SavedState);

    private enum ScopeKind { State, Clip, Layer }
    private enum EncoderStatus { Recording, Finished, Disposed }
}
