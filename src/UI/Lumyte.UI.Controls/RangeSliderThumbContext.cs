namespace Lumyte.UI.Controls;

/// <summary>The accepted value and identity of a decorative range thumb.</summary>
/// <param name="Value">The accepted value for this thumb.</param>
/// <param name="IsUpper">Whether this is the upper rather than lower thumb.</param>
public readonly record struct RangeSliderThumbContext(double Value, bool IsUpper);
