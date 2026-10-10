namespace Lumyte.Input.Actions;
/// <summary>Defines rebind options.</summary>
/// <param name = "Timeout">The Timeout value.</param>
/// <param name = "AxisThreshold">The AxisThreshold value.</param>
/// <param name = "CancelKey">The CancelKey value.</param>
public sealed record RebindOptions(TimeSpan Timeout, float AxisThreshold = 0.6f, Key CancelKey = Key.Escape);
