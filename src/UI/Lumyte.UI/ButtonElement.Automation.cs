using System.Numerics;

namespace Lumyte.UI;

/// <summary>Exposes the mounted button capabilities and press state.</summary>
public partial class ButtonElement
{
    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.Click;

    /// <inheritdoc/>
    protected override bool IsAutomationPressed => IsPressed;

    /// <inheritdoc/>
    protected override bool HasAutomationActivePress => _pressedPointer is not null || _pressedKey is not null;

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetAutomationChildren() => GetChildren(Widget.Children);

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) =>
        InputBounds.Contains(localPosition + InputOrigin) ? UiInputResult.Handled : UiInputResult.StopPropagation;
}
