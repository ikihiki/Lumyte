using Lumyte.Mathematics.Colors;
using ControlWidget = Lumyte.UI.Controls.Compose.Definitions.Slider;

namespace Lumyte.UI.Controls;

/// <summary>Owns accepted Slider state independently of its declaration.</summary>
/// <param name="widget">The passive configuration.</param>
public sealed class SliderElement(ControlWidget widget) : SliderElementBase<ControlWidget>(widget)
{
    /// <summary>Gets the accepted value.</summary>
    public double Value { get; private set; } = Adopt(widget);

    /// <inheritdoc/>
    protected override bool HasTrackVisual => Widget.TrackTemplate is not null;

    /// <inheritdoc/>
    protected override bool HasThumbVisual => Widget.ThumbTemplate is not null;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.SetValue;

    /// <inheritdoc/>
    protected override UiNumericState? NumericState => new(Minimum, Maximum, Increment, Value, null, null);

    /// <inheritdoc/>
    protected override double Minimum => Widget.Min;

    /// <inheritdoc/>
    protected override double Maximum => Widget.Max;

    /// <inheritdoc/>
    protected override double Increment => Widget.Step;

    /// <inheritdoc/>
    protected override UiOrientation Orientation => Widget.Orientation;

    /// <inheritdoc/>
    protected override bool Reversed => Widget.IsReversed;

    /// <inheritdoc/>
    protected override SRgba<float> TrackColor => Widget.TrackColor;

    /// <inheritdoc/>
    protected override SRgba<float> ThumbColor => Widget.ThumbColor;

    /// <inheritdoc/>
    protected override double ActiveValue => Value;

    /// <inheritdoc/>
    protected override IEnumerable<double> ThumbValues => [Value];

    /// <inheritdoc/>
    protected override double? AutomationValue => SliderScale.Fraction(Value, Minimum, Maximum);

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Lumyte.UI.Compose.Definitions.Widget next, UiElementBuildContext context)
    {
        var proposed = (ControlWidget)next;
        if (proposed.TrackTemplate is null && proposed.ThumbTemplate is null)
        {
            return null;
        }

        SliderScale.Validate(proposed.Min, proposed.Max, proposed.Step);
        if (!double.IsFinite(proposed.Value) || !Enum.IsDefined(proposed.Orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(proposed.Value));
        }

        double value = ReferenceEquals(proposed, Widget) ? Value : Adopt(proposed);
        var parts = new List<VisualPart>();
        if (proposed.TrackTemplate is { } track)
        {
            parts.Add(new("track", track(new(value)), size => (size, System.Numerics.Vector2.Zero)));
        }

        if (proposed.ThumbTemplate is { } thumb)
        {
            parts.Add(ControlVisuals.ThumbPart("thumb", thumb(new(value)), value, proposed.Min, proposed.Max, proposed.Orientation, proposed.IsReversed));
        }

        return parts.Count == 0 ? null : new UiElementContent(new ControlVisuals(parts.ToArray()), ownsContent: true);
    }

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiSetNumericInput input)
        {
            ControlWidget declaration = Widget;
            if ((!context.RequestFocus() && declaration.Focusable) || !IsMounted || (declaration.Focusable && !context.IsFocused) || !ReferenceEquals(declaration, Widget))
            {
                return UiInputResult.StopPropagation;
            }

            Accept(SliderScale.Snap(input.Value, Minimum, Maximum, Increment));
            return UiInputResult.Handled;
        }

        return base.OnInput(in context);
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(ControlWidget next, UiElementUpdateContext context)
    {
        if (ReferenceEquals(next, Widget))
        {
            return null;
        }

        double value = Adopt(next);
        context.CancelInput();
        return () =>
        {
            Value = value;
            InvalidateDrawing();
        };
    }

    /// <inheritdoc/>
    protected override void Accept(double value)
    {
        double accepted = value;
        if (accepted == Value)
        {
            return;
        }

        Value = accepted;
        if (HasThumbVisual || HasTrackVisual)
        {
            InvalidateContent();
            InvalidateLayout();
        }

        InvalidateDrawing();
        Widget.OnChanged?.Invoke(accepted);
    }

    /// <inheritdoc/>
    protected override void Committed() => Widget.OnCommitted?.Invoke(Value);

    private static double Adopt(ControlWidget widget) => Math.Clamp(widget.Value, widget.Min, widget.Max);
}
