namespace Lumyte.UI.Controls;

/// <summary>The accepted per-instance slider value used to build a decorative thumb.</summary>
/// <param name="Value">The accepted value, not the declaration's initial value.</param>
public readonly record struct SliderThumbContext(double Value);
