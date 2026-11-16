using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using BarWidget = Lumyte.UI.Controls.Compose.Definitions.ScrollBar;

namespace Lumyte.UI.Controls;

/// <summary>Owns thumb capture without internal button children.</summary>
/// <param name="widget">The passive configuration.</param>
public sealed class ScrollBarElement(BarWidget widget) : UiElement<BarWidget>(widget)
{
    private long? _pointer;
    private float _anchor;

    /// <inheritdoc/>
    protected override double? AutomationValue => Widget.Controller is { IsDisposed: false } controller && controller.Maximum > 0 ? controller.Offset / controller.Maximum : 0;

    /// <inheritdoc/>
    protected override bool PreservePointerGesture => true;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(BarWidget previous, BarWidget next) => UiInvalidation.All;

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(BarWidget next, UiElementUpdateContext context)
    {
        if (!ReferenceEquals(next.Controller, Widget.Controller) || next.Orientation != Widget.Orientation)
        {
            context.CancelInput();
        }

        return null;
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        if (!constraints.HasBoundedWidth || !constraints.HasBoundedHeight)
        {
            throw new InvalidOperationException("ScrollBar requires finite maxima.");
        }

        return new(constraints.MaxWidth, constraints.MaxHeight);
    }

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        ScrollController controller = Widget.Controller!;
        if (context.Input is UiPointerInput pointer)
        {
            float position = Widget.Orientation == UiOrientation.Vertical ? context.PointerPosition!.Value.Y : context.PointerPosition!.Value.X;
            (float start, float thumb, float travel) = Geometry();
            if (pointer.Phase == UiPointerPhase.Down && context.IsInside && _pointer is null)
            {
                context.RequestFocus();
                if (position >= start && position < start + thumb && context.CapturePointer())
                {
                    _pointer = pointer.PointerId;
                    _anchor = position - start;
                }
                else if (controller.IsAttached)
                {
                    controller.ScrollTo((float)Math.Clamp((double)controller.Offset + (position < start ? -controller.Viewport : controller.Viewport), 0, controller.Maximum));
                }

                return UiInputResult.Handled;
            }

            if (pointer.PointerId == _pointer || (context.IsCaptured && pointer.Phase is UiPointerPhase.Up or UiPointerPhase.Cancel))
            {
                if (pointer.Phase is UiPointerPhase.Up or UiPointerPhase.Cancel)
                {
                    _pointer = null;
                }
                else if (pointer.Phase == UiPointerPhase.Move && travel > 0 && controller.IsAttached)
                {
                    controller.ScrollTo(Math.Clamp((position - _anchor) / travel, 0, 1) * controller.Maximum);
                }

                return UiInputResult.Handled;
            }
        }

        if (context.Input is UiKeyInput { IsDown: true } key && context.IsFocused && controller.IsAttached)
        {
            float? target = key.Key switch
            {
                Key.Home => 0,
                Key.End => controller.Maximum,
                Key.PageUp => Math.Max(0, controller.Offset - controller.Viewport),
                Key.PageDown => (float)Math.Min(controller.Maximum, (double)controller.Offset + controller.Viewport),
                Key.ArrowUp or Key.ArrowLeft => controller.Offset - Widget.LineStep,
                Key.ArrowDown or Key.ArrowRight => (float)Math.Min(controller.Maximum, (double)controller.Offset + Widget.LineStep),
                _ => null,
            };
            if (target is { } value && (key.IsRepeat || context.CaptureKey()))
            {
                controller.ScrollTo(value);
                return UiInputResult.Handled;
            }
        }

        return UiInputResult.Unhandled;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
    {
        if (!state.IsCaptured && state.PointerId == _pointer)
        {
            _pointer = null;
        }
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        (float start, float thumb, _) = Geometry();
        canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), Widget.TrackColor);
        canvas.FillRect(Widget.Orientation == UiOrientation.Vertical ? new(0, start, ContentSize.X, thumb) : new(start, 0, thumb, ContentSize.Y), Widget.ThumbColor);
    }

    private (float Start, float Thumb, float Travel) Geometry()
    {
        ScrollController controller = Widget.Controller!;
        float length = Widget.Orientation == UiOrientation.Vertical ? ContentSize.Y : ContentSize.X;
        float thumb = controller.Extent <= 0 || !controller.IsAttached ? length : Math.Min(length, Math.Max(Widget.MinimumThumbSize, length * (controller.Viewport / controller.Extent)));
        float travel = Math.Max(0, length - thumb);
        float start = controller.Maximum <= 0 ? 0 : travel * (controller.Offset / controller.Maximum);
        return (start, thumb, travel);
    }
}
