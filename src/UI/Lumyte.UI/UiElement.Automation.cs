using System.Numerics;

namespace Lumyte.UI;

/// <summary>Provides semantic state and side-effect-free routing for mounted instances.</summary>
public partial class UiElement
{
    /// <summary>Gets the nonreused identity of this mounted instance.</summary>
    public UiAutomationId AutomationId { get; private set; }

    internal UiAutomationExposure EffectiveAutomationExposure => AutomationExposure;

    internal IEnumerable<UiElement> AutomationChildren => GetAutomationChildren();

    internal UiAutomationActions SupportedActions => AutomationActions;

    internal bool AutomationPressed => IsAutomationPressed;

    internal bool AutomationActivePress => HasAutomationActivePress;

    /// <summary>Gets the metadata and subtree exposure policy.</summary>
    protected virtual UiAutomationExposure AutomationExposure => UiAutomationExposure.Default;

    /// <summary>Gets actual component capabilities, independently of role.</summary>
    protected virtual UiAutomationActions AutomationActions => Widget.Focusable ? UiAutomationActions.Focus : UiAutomationActions.None;

    /// <summary>Gets a value indicating whether there is component-specific pressed presentation.</summary>
    protected virtual bool IsAutomationPressed => false;

    /// <summary>Gets a value indicating whether there is a component-owned active press, including outside capture.</summary>
    protected virtual bool HasAutomationActivePress => false;

    internal UiInputResult InvokePointerProbe(Vector2 localPosition) => OnProbePointerDown(localPosition);

    /// <summary>Returns distinct direct semantic children in preorder; the default includes all registered collections.</summary>
    /// <returns>The children exposed to UI tools.</returns>
    protected virtual IEnumerable<UiElement> GetAutomationChildren() => Children;

    /// <summary>Predicts this component's pointer Down routing without side effects; input consumers override this alongside OnInput.</summary>
    /// <param name="localPosition">The input position relative to this component.</param>
    /// <returns>The same propagation decision as ordinary pointer Down.</returns>
    protected virtual UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Unhandled;
}
