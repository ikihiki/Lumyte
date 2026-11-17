using System.Numerics;

namespace Lumyte.UI.Collections;

internal sealed class CollectionItemElement(CollectionItem widget) : UiElement<CollectionItem>(widget)
{
    private readonly UiActivationRecognizer _activation = new();

    protected override bool IsTabStop => Widget.TabStop(this);

    protected override bool? AutomationSelected => Widget.Selected();

    protected override UiAutomationId? AutomationSelectionGroup => Widget.SelectionOwner();

    protected override string? AutomationOptionKey => Widget.Key;

    protected override UiAutomationActions AutomationActions => UiAutomationActions.Click | UiAutomationActions.Focus;

    protected override bool IsAutomationPressed => _activation.IsPressed;

    protected override bool HasAutomationActivePress => _activation.HasActivePress;

    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

    protected override UiInputResult OnInput(in UiInputContext context)
    {
        UiInputResult navigation = Widget.Navigate(this, in context);
        if (navigation != UiInputResult.Unhandled)
        {
            return navigation;
        }

        return _activation.HandleInput(in context, context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } point ? OnProbePointerDown(point) : UiInputResult.Unhandled, () => Widget.Activate(this));
    }

    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state);

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        UiElement child = Children[0];
        Vector2 size = LayoutChild(child, constraints);
        SetChildOffset(child, Vector2.Zero);
        return constraints.Constrain(Widget.Orientation == UiOrientation.Vertical && constraints.HasBoundedWidth ? new(constraints.MaxWidth, size.Y) : Widget.Orientation == UiOrientation.Horizontal && constraints.HasBoundedHeight ? new(size.X, constraints.MaxHeight) : size);
    }
}
