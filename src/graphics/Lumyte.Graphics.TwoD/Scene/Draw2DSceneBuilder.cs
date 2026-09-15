using System.Numerics;

using Lumyte.Graphics.Text;

namespace Lumyte.Graphics.TwoD;

public sealed class Draw2DSceneBuilder : IDisposable
{
    private readonly float deviceScale;
    private List<Draw2DCommand> commands = [];
    private Draw2DState state = Draw2DState.Identity;
    private readonly Stack<ScopeEntry> scopes = new();
    private bool finished;
    private long nextScope;

    public Draw2DSceneBuilder(float deviceScale = 1)
    {
        if (!float.IsFinite(deviceScale) || deviceScale <= 0)
        { throw new ArgumentOutOfRangeException(nameof(deviceScale)); }
        this.deviceScale = deviceScale;
    }
    public void FillRectangle(Rect rectangle, Brush brush) => Add(new Draw2DShapeCommand(Draw2DShapeKind.Rectangle, rectangle.Validate(), default, Require(brush), state));
    public void FillRoundedRectangle(Rect rectangle, CornerRadius radius, Brush brush) => Add(new Draw2DShapeCommand(Draw2DShapeKind.RoundedRectangle, rectangle.Validate(), radius.Validate(), Require(brush), state));
    public void FillRoundedRectangle(Rect rectangle, float radius, Brush brush) => FillRoundedRectangle(rectangle, new CornerRadius(radius), brush);
    public void FillEllipse(Rect bounds, Brush brush) => Add(new Draw2DShapeCommand(Draw2DShapeKind.Ellipse, bounds.Validate(), default, Require(brush), state));
    public void DrawLine(Vector2 start, Vector2 end, float width, Brush brush) => StrokePath(new PathBuilder().MoveTo(start).LineTo(end).Build(), brush, new StrokeStyle(width));
    public void DrawPath(PathGeometry path, Brush brush, FillRule fillRule = FillRule.NonZero)
    { ArgumentNullException.ThrowIfNull(path); ValidateFill(fillRule); Add(new Draw2DPathCommand(path, Require(brush), fillRule, null, state)); }
    public void StrokePath(PathGeometry path, Brush brush, StrokeStyle stroke)
    { ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(stroke); Add(new Draw2DPathCommand(path, Require(brush), FillRule.NonZero, stroke, state)); }
    public void DrawGeometry(PolygonGeometry geometry, Matrix3x2 transform, Brush brush)
    { ArgumentNullException.ThrowIfNull(geometry); ValidateTransform(transform); Add(new Draw2DGeometryCommand(geometry, Require(brush), new(transform * state.Transform, state.Clips))); }
    public void DrawImage(Draw2DImageSource source, Rect destination, Rect? sourceRectangle = null, Color? tint = null, Draw2DImageSampling sampling = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Add(new Draw2DImageCommand(source, destination.Validate(), (sourceRectangle ?? new Rect(0, 0, source.Description.Width, source.Description.Height)).Validate(), (tint ?? Color.White).Validate(), sampling, state));
    }
    public void DrawDistanceField(DistanceFieldUploadData data, Rect destination, Brush brush)
    { ArgumentNullException.ThrowIfNull(data); Add(new Draw2DDistanceFieldCommand(data, destination.Validate(), Require(brush), state)); }
    public void DrawText(TextDrawData data, Vector2 origin, Brush brush)
    { ArgumentNullException.ThrowIfNull(data); if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y)) { throw new ArgumentOutOfRangeException(nameof(origin)); } Add(new Draw2DTextCommand(data, origin, Require(brush), state)); }
    public void DrawScene(Draw2DScene content)
    { ArgumentNullException.ThrowIfNull(content); RequireLogicalChild(content); Add(new Draw2DSceneCommand(content, state)); }
    public Draw2DScope BeginState() => Begin(null);
    public Draw2DScope BeginClip(Rect rectangle)
    {
        rectangle.Validate();
        var scope = Begin(null);
        state = new(state.Transform, [.. state.Clips, new Draw2DClip(rectangle, null, FillRule.NonZero, state.Transform)]);
        return scope;
    }
    public Draw2DScope BeginClip(PathGeometry path, FillRule fillRule = FillRule.NonZero)
    {
        ArgumentNullException.ThrowIfNull(path);
        ValidateFill(fillRule);
        var scope = Begin(null);
        state = new(state.Transform, [.. state.Clips, new Draw2DClip(null, path, fillRule, state.Transform)]);
        return scope;
    }
    public Draw2DScope BeginLayer(Draw2DLayerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!float.IsFinite(options.Opacity) || options.Opacity is < 0 or > 1)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        if (!float.IsFinite(options.BlurRadius) || options.BlurRadius < 0)
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        if (!Enum.IsDefined(options.CompositeMode))
        { throw new ArgumentOutOfRangeException(nameof(options)); }
        options.Bounds?.Validate();
        options.Mask?.Destination.Validate();
        if (options.Shadow is { } shadow)
        {
            shadow.Color.Validate();
            if (!float.IsFinite(shadow.BlurRadius) || shadow.BlurRadius < 0 || !float.IsFinite(shadow.Offset.X) || !float.IsFinite(shadow.Offset.Y))
            { throw new ArgumentOutOfRangeException(nameof(options)); }
        }
        return Begin(options);
    }
    public void SetTransform(Matrix3x2 transform) { EnsureOpen(); ValidateTransform(transform); state = new(transform, state.Clips); }
    public void Transform(Matrix3x2 transform) { EnsureOpen(); ValidateTransform(transform); state = new(transform * state.Transform, state.Clips); }
    public Draw2DScene Finish()
    {
        EnsureOpen();
        if (scopes.Count != 0)
        { throw new InvalidOperationException("All drawing scopes must be closed before finishing."); }
        finished = true;
        var result = new Draw2DScene(commands, deviceScale);
        commands.Clear();
        return result;
    }
    public void Dispose() { finished = true; commands.Clear(); scopes.Clear(); }

    private Draw2DScope Begin(Draw2DLayerOptions? layer)
    {
        EnsureOpen();
        var id = ++nextScope;
        scopes.Push(new(id, state, commands, layer));
        if (layer is not null)
        { commands = []; state = Draw2DState.Identity; }
        return new(this, id);
    }
    internal void End(long id)
    {
        EnsureOpen();
        if (scopes.Count == 0 || scopes.Peek().Id != id)
        { throw new InvalidOperationException("Drawing scopes must be disposed in reverse creation order."); }
        var entry = scopes.Pop();
        var child = commands;
        commands = entry.Commands;
        state = entry.State;
        if (entry.Layer is not null)
        { commands.Add(new Draw2DLayerCommand(entry.Layer, new(child, 1), state)); }
    }
    private void Add(Draw2DCommand command) { EnsureOpen(); commands.Add(command); }
    private void EnsureOpen() { if (finished) { throw new ObjectDisposedException(nameof(Draw2DSceneBuilder)); } }
    private static Brush Require(Brush brush) => brush ?? throw new ArgumentNullException(nameof(brush));
    private static void ValidateFill(FillRule rule) { if (!Enum.IsDefined(rule)) { throw new ArgumentOutOfRangeException(nameof(rule)); } }
    internal static void RequireLogicalChild(Draw2DScene scene) { if (scene.DeviceScale != 1) { throw new ArgumentException("A child scene must use deviceScale 1.", nameof(scene)); } }
    internal static void ValidateTransform(Matrix3x2 matrix)
    {
        if (!float.IsFinite(matrix.M11) || !float.IsFinite(matrix.M12) || !float.IsFinite(matrix.M21) || !float.IsFinite(matrix.M22) || !float.IsFinite(matrix.M31) || !float.IsFinite(matrix.M32))
        { throw new ArgumentOutOfRangeException(nameof(matrix)); }
    }
    private sealed record ScopeEntry(long Id, Draw2DState State, List<Draw2DCommand> Commands, Draw2DLayerOptions? Layer);
}

public sealed class Draw2DScope : IDisposable
{
    private Draw2DSceneBuilder? owner;
    private readonly long id;
    internal Draw2DScope(Draw2DSceneBuilder owner, long id) { this.owner = owner; this.id = id; }
    public void Dispose() { if (owner is { } current) { current.End(id); owner = null; } }
}
