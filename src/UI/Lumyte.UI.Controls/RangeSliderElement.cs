using Lumyte.Mathematics.Colors;
using ControlWidget = Lumyte.UI.Controls.Compose.Definitions.RangeSlider;

namespace Lumyte.UI.Controls;

/// <summary>Owns accepted RangeSlider state independently of its declaration.</summary>
/// <param name="widget">The passive configuration.</param>
public sealed class RangeSliderElement(ControlWidget widget) : SliderElementBase<ControlWidget>(widget)
{
    private bool _upper;

    /// <summary>Gets the accepted value.</summary>
    public SliderRange Value { get; private set; } = Adopt(widget);

    /// <summary>Gets a value indicating whether the upper thumb is selected.</summary>
    public bool IsUpperThumbSelected => _upper;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.SetRange;

    /// <inheritdoc/>
    protected override UiNumericState? NumericState => new(Minimum, Maximum, Increment, null, Value.Lower, Value.Upper);

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
    protected override double ActiveValue => _upper ? Value.Upper : Value.Lower;

    /// <inheritdoc/>
    protected override IEnumerable<double> ThumbValues => [Value.Lower, Value.Upper];

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

            SliderRange accepted = new(SliderScale.Snap(input.Value, Minimum, Maximum, Increment), SliderScale.Snap(input.Upper!.Value, Minimum, Maximum, Increment));
            if (accepted != Value)
            {
                Value = accepted;
                InvalidateDrawing();
                Widget.OnChanged?.Invoke(accepted);
            }

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

        SliderRange value = Adopt(next);
        context.CancelInput();
        return () =>
        {
            Value = value;
            InvalidateDrawing();
        };
    }

    /// <inheritdoc/>
    protected override void SelectOtherThumb() => _upper = !_upper;

    /// <inheritdoc/>
    protected override void SelectThumb(double fraction)
    {
        double lower = SliderScale.Fraction(Value.Lower, Minimum, Maximum);
        double upper = SliderScale.Fraction(Value.Upper, Minimum, Maximum);
        double a = Math.Abs(fraction - lower);
        double b = Math.Abs(fraction - upper);
        if (a != b)
        {
            _upper = b < a;
        }
    }

    /// <inheritdoc/>
    protected override void Accept(double value)
    {
        SliderRange accepted = _upper ? new(Value.Lower, Math.Max(Value.Lower, value)) : new(Math.Min(value, Value.Upper), Value.Upper);
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

    private static SliderRange Adopt(ControlWidget widget) => new(Math.Clamp(widget.Value.Lower, widget.Min, widget.Max), Math.Clamp(widget.Value.Upper, widget.Min, widget.Max));
}
