using System.Numerics;

namespace Lumyte.UI.Collections;

internal sealed class CollectionItemElement(CollectionItem widget) : UiElement<CollectionItem>(widget)
{
    private readonly UiActivationRecognizer _activation = new();
    private bool _glyphPress;

    protected override UiTreeItemState? AutomationTreeItem => Widget.TreeItem;

    protected override bool? AutomationExpanded => Widget.Expanded?.Invoke();

    protected override bool IsTabStop => Widget.TabStop(this);

    protected override Lumyte.Graphics.Drawing2D.Rect2D AutomationActionRegion
    {
        get
        {
            float start = Widget.Expand is null ? 0 : Math.Clamp(Widget.GlyphStart + 16, 0, ContentSize.X);
            return new(start, 0, ContentSize.X - start, ContentSize.Y);
        }
    }

    protected override bool? AutomationSelected => Widget.PassiveRow ? null : Widget.Selected();

    protected override UiAutomationId? AutomationSelectionGroup => Widget.PassiveRow ? null : Widget.SelectionOwner();

    protected override string? AutomationOptionKey => Widget.PassiveRow ? null : Widget.Key;

    protected override UiAutomationActions AutomationActions => Widget.PassiveRow ? UiAutomationActions.None : UiAutomationActions.Focus
        | (Widget.TreeItem is null || (AutomationActionRegion.Width > 0 && AutomationActionRegion.Height > 0) ? UiAutomationActions.Click : UiAutomationActions.None)
        | (Widget.Expand is null ? UiAutomationActions.None : UiAutomationActions.ExpandCollapse);

    protected override bool IsAutomationPressed => _activation.IsPressed;

    protected override bool HasAutomationActivePress => _activation.HasActivePress;

    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (Widget.PassiveRow)
        {
            return UiInputResult.StopPropagation;
        }

        if (context.Input is UiExpandInput expansion && Widget.Expand is { } expand)
        {
            expand(expansion.IsExpanded);
            return UiInputResult.Handled;
        }

        if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } down)
        {
            _glyphPress = Widget.Expand is not null && down.X >= Widget.GlyphStart && down.X < Widget.GlyphStart + 16;
        }

        UiInputResult navigation = Widget.HierarchyNavigate?.Invoke(this, in context) ?? UiInputResult.Unhandled;
        if (navigation == UiInputResult.Unhandled)
        {
            navigation = Widget.Navigate(this, in context);
        }

        if (navigation != UiInputResult.Unhandled)
        {
            return navigation;
        }

        bool pointerActivation = context.Input is UiPointerInput;
        bool? glyphInside = _glyphPress && context.PointerPosition is { } glyphPoint
            ? context.IsInside && glyphPoint.X >= Widget.GlyphStart && glyphPoint.X < Widget.GlyphStart + 16 : null;
        return _activation.HandleInput(
            in context,
            context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } point ? OnProbePointerDown(point) : UiInputResult.Unhandled,
            () =>
            {
                if (_glyphPress && pointerActivation)
                {
                    Widget.Expand?.Invoke(!(Widget.Expanded?.Invoke() ?? false));
                }
                else
                {
                    Widget.Activate(this);
                }
            },
            glyphInside);
    }

    protected override void OnRecord(Lumyte.Graphics.Drawing2D.DrawingCanvas2D canvas)
    {
        if (Widget.Expanded is not { } expanded)
        {
            return;
        }

        float x = Widget.GlyphStart + 4;
        float y = Math.Max(0, (ContentSize.Y - 2) / 2);
        var color = new Lumyte.Mathematics.Colors.SRgba<float>(0.6f, 0.6f, 0.6f, 1);
        canvas.FillRect(new(x, y, 8, 2), color);
        if (!expanded())
        {
            canvas.FillRect(new(x + 3, Math.Max(0, y - 3), 2, 8), color);
        }
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
