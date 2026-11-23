using System.Numerics;
using TooltipWidget = Lumyte.UI.Controls.Compose.Definitions.Tooltip;

namespace Lumyte.UI.Controls;

/// <summary>Owns only a passive popup request; anchor input and focus remain on the child.</summary>
public class TooltipElement : UiElement<TooltipWidget>
{
    private readonly PopupReservation _popup = new();

    /// <summary>Initializes a new instance of the <see cref="TooltipElement"/> class.</summary>
    /// <param name="widget">The passive declaration.</param>
    protected internal TooltipElement(TooltipWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets a value indicating whether this instance owns a pending or open request.</summary>
    public bool IsOpen => _popup.IsOpen;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override void OnMounted() => Reconcile();

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(TooltipWidget previous)
    {
        _popup.Close();
        Reconcile();
    }

    /// <inheritdoc/>
    protected override void OnUnmounted() => _popup.Close();

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = Vector2.Zero;
        foreach (UiElement child in Children)
        {
            size = LayoutChild(child, constraints.Loosen());
            SetChildOffset(child, Vector2.Zero);
        }

        return constraints.Constrain(size);
    }

    private void Reconcile()
    {
        if (Widget.IsOpen && Widget.PopupHost is { } host && Widget.CreatePopup is { } create)
        {
            _popup.Open(host, this, Widget.PopupSize, create, true);
        }
    }
}
