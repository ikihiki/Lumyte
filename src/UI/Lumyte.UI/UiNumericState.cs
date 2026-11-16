namespace Lumyte.UI;

/// <summary>Detached finite numeric state; unlike Value, these values are not normalized.</summary>
/// <param name="Minimum">The inclusive minimum.</param>
/// <param name="Maximum">The inclusive maximum.</param>
/// <param name="Step">The positive slider step, or null for an editor.</param>
/// <param name="Value">The accepted scalar, or null for an empty editor or range.</param>
/// <param name="Lower">The range's lower value, otherwise null.</param>
/// <param name="Upper">The range's upper value, otherwise null.</param>
/// <param name="IsValid">Whether the numeric editor buffer is valid; no text is disclosed.</param>
public sealed record UiNumericState(double Minimum, double Maximum, double? Step, double? Value, double? Lower, double? Upper, bool IsValid = true)
{
    internal void Validate()
    {
        if (!double.IsFinite(Minimum) || !double.IsFinite(Maximum) || Minimum > Maximum
            || (Step is { } step && (!double.IsFinite(step) || step <= 0))
            || !Valid(Value) || !Valid(Lower) || !Valid(Upper)
            || Lower.HasValue != Upper.HasValue || Lower > Upper || (Value.HasValue && Lower.HasValue))
        {
            throw new InvalidOperationException("Numeric semantics must contain finite ordered values within their bounds.");
        }
    }

    private bool Valid(double? value) => value is null || (double.IsFinite(value.Value) && value >= Minimum && value <= Maximum);
}
