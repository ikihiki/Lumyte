namespace Lumyte.UI.Controls;

/// <summary>An atomic ordered range of finite numeric values.</summary>
/// <param name="Lower">The lower endpoint.</param>
/// <param name="Upper">The upper endpoint.</param>
public readonly record struct SliderRange(double Lower, double Upper);
