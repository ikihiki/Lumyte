using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Shares content layout, drawing and the ordinary activation recognizer.</summary>
/// <typeparam name="TWidget">The passive control declaration.</typeparam>
/// <param name="widget">The initial declaration.</param>
public abstract class SelectionControlElement<TWidget>(TWidget widget) : UiElement<TWidget>(widget)
    where TWidget : WidgetDefinition
{
    private readonly UiActivationRecognizer _activation = new();

    /// <summary>Gets a value indicating whether a pointer inside or keyboard activation is held.</summary>
    public bool IsPressed => _activation.IsPressed;

    /// <summary>Gets the ordinary content slot.</summary>
    protected abstract WidgetChildren ContentSlot { get; }

    /// <summary>Gets the background slot.</summary>
    protected abstract WidgetChildren BackgroundSlot { get; }

    /// <summary>Gets the mark extent.</summary>
    protected abstract float MarkSize { get; }

    /// <summary>Gets the content gap.</summary>
    protected abstract float Gap { get; }

    /// <summary>Gets the mark foreground.</summary>
    protected abstract SRgba<float> Foreground { get; }

    /// <summary>Gets the mark track.</summary>
    protected abstract SRgba<float> Background { get; }

    /// <summary>Gets the accepted check state, including mixed.</summary>
    protected abstract bool? CheckedValue { get; }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => true;

    /// <inheritdoc/>
    protected override bool IsAutomationPressed => IsPressed;

    /// <inheritdoc/>
    protected override bool HasAutomationActivePress => _activation.HasActivePress;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.Click | UiAutomationActions.Check;

    /// <inheritdoc/>
    protected override bool? AutomationChecked => CheckedValue;

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(WidgetDefinition other) => false;

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => GetChildren(ContentSlot);

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetAutomationChildren() => GetChildren(ContentSlot);

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiCheckInput check)
        {
            if (!context.RequestFocus() && Widget.Focusable)
            {
                return UiInputResult.StopPropagation;
            }

            if (Surface is null)
            {
                return UiInputResult.StopPropagation;
            }

            CommitChecked(check.IsChecked);
            return UiInputResult.Handled;
        }

        return _activation.HandleInput(in context, context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } position ? OnProbePointerDown(position) : UiInputResult.Unhandled, Activate);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state);

    /// <summary>Commits one ordinary user activation.</summary>
    protected abstract void Activate();

    /// <summary>Commits a desired user check state.</summary>
    /// <param name="value">The requested state.</param>
    protected abstract void CommitChecked(bool value);

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        float leading = MarkSize + (GetChildren(ContentSlot).Count == 0 ? 0 : Gap);
        Vector2 size = new(leading, MarkSize);
        foreach (UiElement child in GetChildren(ContentSlot))
        {
            var childConstraints = new UiBoxConstraints(0, Math.Max(0, constraints.MaxWidth - leading), 0, constraints.MaxHeight);
            Vector2 content = LayoutChild(child, childConstraints);
            SetChildOffset(child, new(leading, 0));
            size = new(leading + content.X, Math.Max(MarkSize, content.Y));
        }

        size = constraints.Constrain(size);
        foreach (UiElement child in GetChildren(BackgroundSlot))
        {
            LayoutChild(child, UiBoxConstraints.Tight(size));
            SetChildOffset(child, Vector2.Zero);
        }

        return size;
    }

    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
        foreach (UiElement child in GetChildren(BackgroundSlot))
        {
            RecordChild(child, canvas);
        }

        RecordSelf(canvas);
        foreach (UiElement child in GetChildren(ContentSlot))
        {
            RecordChild(child, canvas);
        }
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        float size = Math.Min(MarkSize, Math.Min(ContentSize.X, ContentSize.Y));
        if (size <= 0)
        {
            return;
        }

        canvas.FillRect(new(0, 0, size, size), Background);
        if (CheckedValue is true)
        {
            canvas.FillRect(new(size * 0.25f, size * 0.25f, size * 0.5f, size * 0.5f), Foreground);
        }
        else if (CheckedValue is null)
        {
            canvas.FillRect(new(size * 0.2f, size * 0.4f, size * 0.6f, size * 0.2f), Foreground);
        }
    }
}
