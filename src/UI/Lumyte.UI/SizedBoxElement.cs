using System.Numerics;
using SizedBoxWidget = Lumyte.UI.Compose.Definitions.SizedBox;

namespace Lumyte.UI;

internal sealed class SizedBoxElement(SizedBoxWidget widget) : UiElement<SizedBoxWidget>(widget)
{
    protected override bool SupportsTransitionCapture => Widget.GetType() == typeof(SizedBoxWidget);

    protected override bool CanReuseTransitionCapture(Compose.Definitions.Widget other) => other.GetType() == typeof(SizedBoxWidget);

    protected override UiInvalidation CompareConfiguration(SizedBoxWidget previous, SizedBoxWidget next) => UiInvalidation.None;

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
