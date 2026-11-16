using System.Numerics;
using static Lumyte.UI.Compose.Definitions;

namespace Lumyte.UI;

internal sealed class C4BoxElement<TWidget>(TWidget widget) : UiElement<TWidget>(widget)
    where TWidget : SizedBox
{
    protected override UiInvalidation CompareConfiguration(TWidget previous, TWidget next) => previous switch
    {
        Expanded before when next is Expanded after => before.Weight == after.Weight ? UiInvalidation.None : UiInvalidation.Layout,
        GridCell before when next is GridCell after => before.Row == after.Row && before.Column == after.Column && before.RowSpan == after.RowSpan && before.ColumnSpan == after.ColumnSpan ? UiInvalidation.None : UiInvalidation.Layout,
        _ => UiInvalidation.Layout,
    };

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        UiElementChildren children = GetChildren(Widget.Children);
        if (children.Count == 0)
        {
            return constraints.Constrain(Vector2.Zero);
        }

        UiElement child = children[0];
        Vector2 size = LayoutChild(child, constraints);
        SetChildOffset(child, Vector2.Zero);
        return constraints.Constrain(size);
    }
}
