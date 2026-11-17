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
        InvalidateDrawing();
        Widget.OnChanged?.Invoke(accepted);
    }

    /// <inheritdoc/>
    protected override void Committed() => Widget.OnCommitted?.Invoke(Value);

    private static double Adopt(ControlWidget widget) => Math.Clamp(widget.Value, widget.Min, widget.Max);
}
