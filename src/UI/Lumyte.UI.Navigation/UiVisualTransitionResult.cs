namespace Lumyte.UI.Navigation;

/// <summary>A closed visual outcome independent of logical commit and GPU retirement.</summary>
/// <param name="Status">The visual outcome.</param>
/// <param name="Code">The stable code without keys, images or exception text.</param>
public sealed record UiVisualTransitionResult(UiVisualTransitionStatus Status, string Code);
