namespace Lumyte.UI;

/// <summary>Describes hit geometry and pointer activation without delivering input.</summary>
/// <param name="HitElement">The deepest input hit.</param>
/// <param name="ActivationTarget">The first accepting component with Click capability, if any.</param>
public readonly record struct UiPointerProbe(UiAutomationId? HitElement, UiAutomationId? ActivationTarget);
