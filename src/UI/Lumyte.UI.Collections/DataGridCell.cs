using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal sealed class DataGridCell : Widget
{
    internal DataGridCell()
    {
        Role = UiRole.GridCell;
        Focusable = true;
        ClipToBounds = true;
        Content = CreateChildren(1);
    }

    internal WidgetChildren Content { get; }

    internal required UiGridCellState State { get; init; }

    internal required Action Activate { get; init; }

    internal required Action Focused { get; init; }

    internal required Func<bool> Selected { get; init; }

    internal required Func<bool> TabStop { get; init; }

    internal required Func<UiAutomationId> SelectionOwner { get; init; }

    internal required Func<CellElement, UiKeyInput, bool> Navigate { get; init; }

    protected override UiElement CreateElement() => new CellElement(this);

    internal sealed class CellElement(DataGridCell widget) : UiElement<DataGridCell>(widget)
    {
        private readonly UiActivationRecognizer _activation = new();

        protected override UiGridCellState? AutomationGridCell => Widget.State;

        protected override bool? AutomationSelected => Widget.Selected();

        protected override UiAutomationId? AutomationSelectionGroup => Widget.SelectionOwner();

        protected override bool IsTabStop => Widget.TabStop();

        protected override UiAutomationActions AutomationActions => UiAutomationActions.Click | UiAutomationActions.Focus;

        protected override bool IsAutomationPressed => _activation.IsPressed;

        protected override bool HasAutomationActivePress => _activation.HasActivePress;

        protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiFocusChangedInput { IsFocused: true })
            {
                Widget.Focused();
            }

            if (context.IsFocused && context.Input is UiKeyInput { IsDown: true, IsRepeat: false, Modifiers: UiModifiers.None } key && Widget.Navigate(this, key))
            {
                return UiInputResult.Handled;
            }

            return _activation.HandleInput(in context, context.PointerPosition is { } point ? OnProbePointerDown(point) : UiInputResult.Unhandled, Widget.Activate);
        }

        protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state);

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = LayoutChild(Children[0], constraints);
            SetChildOffset(Children[0], Vector2.Zero);
            return constraints.Constrain(size);
        }
    }
}
