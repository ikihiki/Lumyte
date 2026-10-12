using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Records ordered immutable drawing commands without submitting GPU work.</summary>
public abstract class DrawingCanvas2D
{
    private readonly List<DrawingCommand2D> _commands = [];
    private readonly Stack<DrawingState2D> _savedStates = [];
    private DrawingState2D _state = new(Matrix3x2.Identity, ClipStack2D.Empty);
    private bool _recording;

    /// <summary>Gets or sets the current immutable state for a specialized recorder.</summary>
    protected DrawingState2D State { get => _state; set => _state = value; }

    /// <summary>Gets the save stack depth.</summary>
    protected int SaveDepth => _savedStates.Count;

    /// <summary>Gets the lower save boundary of the current recording scope.</summary>
    protected virtual int SaveBoundary => 0;

    /// <summary>Begins a fresh recording; nested recordings are rejected.</summary>
    public void Begin()
    {
        if (_recording)
        {
            throw new InvalidOperationException("The Canvas is already recording.");
        }

        Reset();
        _recording = true;
    }

    /// <summary>Saves the current transform and clip within the current recording scope.</summary>
    public void Save()
    {
        RequireRecording();
        _savedStates.Push(_state);
    }

    /// <summary>Restores the most recent save in the current recording scope.</summary>
    public void Restore()
    {
        RequireRecording();
        int boundary = SaveBoundary;
        if (_savedStates.Count <= boundary)
        {
            throw new InvalidOperationException("There is no unmatched Save in the current recording scope.");
        }

        _state = _savedStates.Pop();
    }

    /// <summary>Applies a local transform before the existing transform.</summary>
    /// <param name="transform">The finite affine transform.</param>
    public void ConcatTransform(Matrix3x2 transform)
    {
        RequireRecording();
        ModelValidation2D.Matrix(transform, nameof(transform));
        Matrix3x2 combined = transform * _state.Transform;
        ModelValidation2D.Matrix(combined, nameof(transform));
        _state = _state with { Transform = combined };
    }

    /// <summary>Applies a local translation before the existing transform.</summary>
    /// <param name="offset">The finite logical translation.</param>
    public void Translate(Vector2 offset)
    {
        RequireRecording();
        ModelValidation2D.Vector(offset, nameof(offset));
        ConcatTransform(Matrix3x2.CreateTranslation(offset));
    }

    /// <summary>Applies a clockwise local rotation in the Y-down coordinate system.</summary>
    /// <param name="radians">The finite angle in radians.</param>
    public void Rotate(float radians)
    {
        RequireRecording();
        ModelValidation2D.Finite(radians, nameof(radians));
        ConcatTransform(Matrix3x2.CreateRotation(radians));
    }

    /// <summary>Applies a local scale before the existing transform.</summary>
    /// <param name="scale">The finite per-axis scale, including reflection or zero.</param>
    public void Scale(Vector2 scale)
    {
        RequireRecording();
        ModelValidation2D.Vector(scale, nameof(scale));
        ConcatTransform(Matrix3x2.CreateScale(scale));
    }

    /// <summary>Intersects the clip with a rectangle fixed in the current transform.</summary>
    /// <param name="rect">The finite logical rectangle with nonnegative dimensions.</param>
    public void ClipRect(Rect2D rect)
    {
        RequireRecording();
        ModelValidation2D.Rectangle(rect, nameof(rect));
        ClipPath(ModelValidation2D.RectanglePath(rect));
    }

    /// <summary>Intersects the clip with a path fixed in the current transform.</summary>
    /// <param name="path">The immutable logical path.</param>
    /// <param name="rule">The contour fill rule.</param>
    public void ClipPath(Path2D path, FillRule2D rule = FillRule2D.NonZero)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(path);
        ModelValidation2D.EnumValue(rule, nameof(rule));
        _state = _state with { Clip = _state.Clip.Add(new ClipElement2D(path, _state.Transform, rule)) };
    }

    /// <summary>Records FillRect using a solid color.</summary>
    /// <param name="rect">The finite logical rectangle with nonnegative dimensions.</param>
    /// <param name="color">The finite straight-alpha sRGB color.</param>
    public void FillRect(Rect2D rect, Color2D color) => FillRect(rect, PaintGraph2D.Solid(color));

    /// <summary>Records FillRect using an immutable paint graph.</summary>
    /// <param name="rect">The finite logical rectangle with nonnegative dimensions.</param>
    /// <param name="paint">The immutable paint evaluated in logical coordinates.</param>
    public void FillRect(Rect2D rect, PaintGraph2D paint)
    {
        RequireRecording();
        ModelValidation2D.Rectangle(rect, nameof(rect));
        ArgumentNullException.ThrowIfNull(paint);
        _commands.Add(new FillRectCommand2D(rect, paint, _state));
    }

    /// <summary>Records FillRoundedRect using a solid color.</summary>
    /// <param name="rect">The finite logical rectangle with nonnegative dimensions.</param>
    /// <param name="radius">The finite nonnegative logical radius.</param>
    /// <param name="color">The finite straight-alpha sRGB color.</param>
    public void FillRoundedRect(Rect2D rect, float radius, Color2D color) => FillRoundedRect(rect, radius, PaintGraph2D.Solid(color));

    /// <summary>Records FillRoundedRect using an immutable paint graph.</summary>
    /// <param name="rect">The finite logical rectangle with nonnegative dimensions.</param>
    /// <param name="radius">The finite nonnegative logical radius.</param>
    /// <param name="paint">The immutable paint evaluated in logical coordinates.</param>
    public void FillRoundedRect(Rect2D rect, float radius, PaintGraph2D paint)
    {
        RequireRecording();
        ModelValidation2D.Rectangle(rect, nameof(rect));
        ModelValidation2D.Nonnegative(radius, nameof(radius));
        ArgumentNullException.ThrowIfNull(paint);
        _commands.Add(new FillRoundedRectCommand2D(rect, radius, paint, _state));
    }

    /// <summary>Records FillCircle using a solid color.</summary>
    /// <param name="center">The finite logical center.</param>
    /// <param name="radius">The finite nonnegative logical radius.</param>
    /// <param name="color">The finite straight-alpha sRGB color.</param>
    public void FillCircle(Vector2 center, float radius, Color2D color) => FillCircle(center, radius, PaintGraph2D.Solid(color));

    /// <summary>Records FillCircle using an immutable paint graph.</summary>
    /// <param name="center">The finite logical center.</param>
    /// <param name="radius">The finite nonnegative logical radius.</param>
    /// <param name="paint">The immutable paint evaluated in logical coordinates.</param>
    public void FillCircle(Vector2 center, float radius, PaintGraph2D paint)
    {
        RequireRecording();
        ModelValidation2D.Vector(center, nameof(center));
        ModelValidation2D.Nonnegative(radius, nameof(radius));
        ArgumentNullException.ThrowIfNull(paint);
        _commands.Add(new FillCircleCommand2D(center, radius, paint, _state));
    }

    /// <summary>Records DrawLine using a solid color.</summary>
    /// <param name="start">The finite logical start.</param>
    /// <param name="end">The finite logical end.</param>
    /// <param name="color">The finite straight-alpha sRGB color.</param>
    /// <param name="style">The positive stroke width and join/cap settings.</param>
    public void DrawLine(Vector2 start, Vector2 end, Color2D color, StrokeStyle2D style) => DrawLine(start, end, PaintGraph2D.Solid(color), style);

    /// <summary>Records DrawLine using an immutable paint graph.</summary>
    /// <param name="start">The finite logical start.</param>
    /// <param name="end">The finite logical end.</param>
    /// <param name="paint">The immutable paint evaluated in logical coordinates.</param>
    /// <param name="style">The positive stroke width and join/cap settings.</param>
    public void DrawLine(Vector2 start, Vector2 end, PaintGraph2D paint, StrokeStyle2D style)
    {
        RequireRecording();
        ModelValidation2D.Vector(start, nameof(start));
        ModelValidation2D.Vector(end, nameof(end));
        ModelValidation2D.Stroke(style, nameof(style));
        ArgumentNullException.ThrowIfNull(paint);
        _commands.Add(new DrawLineCommand2D(start, end, paint, style with { }, _state));
    }

    /// <summary>Records FillPath using a solid color.</summary>
    /// <param name="path">The immutable logical path.</param>
    /// <param name="color">The finite straight-alpha sRGB color.</param>
    /// <param name="rule">The contour fill rule.</param>
    public void FillPath(Path2D path, Color2D color, FillRule2D rule = FillRule2D.NonZero) => FillPath(path, PaintGraph2D.Solid(color), rule);

    /// <summary>Records FillPath using an immutable paint graph.</summary>
    /// <param name="path">The immutable logical path.</param>
    /// <param name="paint">The immutable paint evaluated in logical coordinates.</param>
    /// <param name="rule">The contour fill rule.</param>
    public void FillPath(Path2D path, PaintGraph2D paint, FillRule2D rule = FillRule2D.NonZero)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(path);
        ModelValidation2D.EnumValue(rule, nameof(rule));
        ArgumentNullException.ThrowIfNull(paint);
        _commands.Add(new FillPathCommand2D(path, paint, rule, _state));
    }

    /// <summary>Records StrokePath using a solid color.</summary>
    /// <param name="path">The immutable logical path.</param>
    /// <param name="color">The finite straight-alpha sRGB color.</param>
    /// <param name="style">The positive stroke width and join/cap settings.</param>
    public void StrokePath(Path2D path, Color2D color, StrokeStyle2D style) => StrokePath(path, PaintGraph2D.Solid(color), style);

    /// <summary>Records StrokePath using an immutable paint graph.</summary>
    /// <param name="path">The immutable logical path.</param>
    /// <param name="paint">The immutable paint evaluated in logical coordinates.</param>
    /// <param name="style">The positive stroke width and join/cap settings.</param>
    public void StrokePath(Path2D path, PaintGraph2D paint, StrokeStyle2D style)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(path);
        ModelValidation2D.Stroke(style, nameof(style));
        ArgumentNullException.ThrowIfNull(paint);
        _commands.Add(new StrokePathCommand2D(path, paint, style with { }, _state));
    }

    /// <summary>Discards all commands and state from the active recording.</summary>
    public void Cancel()
    {
        RequireRecording();
        Reset();
    }

    /// <summary>Validates scopes and completes an immutable recording.</summary>
    /// <returns>The reusable CPU command snapshot.</returns>
    protected DrawList2D CompleteRecording()
    {
        RequireRecording();
        ValidateComplete();
        if (_savedStates.Count != 0)
        {
            throw new InvalidOperationException("All Save calls must be matched before End.");
        }

        var result = new DrawList2D(_commands);
        Reset();
        return result;
    }

    /// <summary>Requires an active recording.</summary>
    protected void RequireRecording()
    {
        if (!_recording)
        {
            throw new InvalidOperationException("Begin must be called before recording operations.");
        }
    }

    /// <summary>Adds a specialized immutable command.</summary>
    /// <param name="command">The captured command.</param>
    protected void AddCommand(DrawingCommand2D command) => _commands.Add(command);

    /// <summary>Validates specialized scopes before completing a recording.</summary>
    protected virtual void ValidateComplete()
    {
    }

    /// <summary>Resets specialized recording state.</summary>
    protected virtual void ResetAdditionalState()
    {
    }

    private void Reset()
    {
        _commands.Clear();
        _savedStates.Clear();
        ResetAdditionalState();
        _state = new DrawingState2D(Matrix3x2.Identity, ClipStack2D.Empty);
        _recording = false;
    }
}
