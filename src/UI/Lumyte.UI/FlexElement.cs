using System.Numerics;
using static Lumyte.UI.Compose.Definitions;

namespace Lumyte.UI;

internal sealed class FlexElement(Flex widget) : UiElement<Flex>(widget)
{
    protected override UiInvalidation CompareConfiguration(Flex previous, Flex next) => previous.Orientation == next.Orientation && previous.Spacing == next.Spacing && previous.MainAxisAlignment == next.MainAxisAlignment && previous.CrossAxisAlignment == next.CrossAxisAlignment ? UiInvalidation.None : UiInvalidation.Layout;

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        bool vertical = Widget.Orientation == UiOrientation.Vertical;
        float maximum = vertical ? constraints.MaxHeight : constraints.MaxWidth;
        float crossMaximum = vertical ? constraints.MaxWidth : constraints.MaxHeight;
        UiElementChildren children = GetChildren(Widget.Children);
        double weight = 0;
        int visible = 0;
        foreach (UiElement child in children)
        {
            if (child.Widget.ParentData is [{ Kind: UiParentDataKind.Flex } expanded])
            {
                if (!float.IsFinite(maximum))
                {
                    throw new InvalidOperationException("Weighted children require a bounded Flex main axis.");
                }

                if (child.LayoutVisible)
                {
                    weight += expanded.Weight;
                }
            }

            visible += child.LayoutVisible ? 1 : 0;
        }

        double occupied = (double)Math.Max(0, visible - 1) * Widget.Spacing;
        double cross = 0;
        foreach (UiElement child in children)
        {
            if (child.Widget.ParentData is [{ Kind: UiParentDataKind.Flex }] && child.LayoutVisible)
            {
                continue;
            }

            Vector2 size = LayoutChild(child, ChildConstraints(0, float.PositiveInfinity, crossMaximum, vertical));
            if (child.LayoutVisible)
            {
                occupied += C4Layout.Main(size, vertical);
                cross = Math.Max(cross, C4Layout.Cross(size, vertical));
            }
        }

        double remaining = float.IsFinite(maximum) ? Math.Max(0, maximum - occupied) : 0;
        double allocated = 0;
        foreach (UiElement child in children)
        {
            if (child.Widget.ParentData is [{ Kind: UiParentDataKind.Flex } expanded] && child.LayoutVisible)
            {
                float share = UiLayoutMath.FiniteFloat(remaining * (expanded.Weight / weight));
                Vector2 size = LayoutChild(child, ChildConstraints(expanded.Fit == UiFlexFit.Tight ? share : 0, share, crossMaximum, vertical));
                allocated += C4Layout.Main(size, vertical);
                cross = Math.Max(cross, C4Layout.Cross(size, vertical));
            }
        }

        Vector2 result = vertical ? UiLayoutMath.FillBounded(constraints, cross, occupied + allocated) : UiLayoutMath.FillBounded(constraints, occupied + allocated, cross);
        (double offset, double extraGap) = C4Layout.Distribution(Widget.MainAxisAlignment, C4Layout.Main(result, vertical) - occupied - allocated, visible);
        bool hasPrevious = false;
        foreach (UiElement child in children)
        {
            if (!child.LayoutVisible)
            {
                SetChildOffset(child, Vector2.Zero);
                continue;
            }

            offset += hasPrevious ? Widget.Spacing + extraGap : 0;
            SetChildOffset(child, C4Layout.Vector(offset, C4Layout.CrossOffset(Widget.CrossAxisAlignment, C4Layout.Cross(result, vertical) - C4Layout.Cross(child.Size, vertical)), vertical));
            offset += C4Layout.Main(child.Size, vertical);
            hasPrevious = true;
        }

        return result;
    }

    private UiBoxConstraints ChildConstraints(float minimum, float maximum, float cross, bool vertical)
    {
        bool stretch = Widget.CrossAxisAlignment == UiCrossAxisAlignment.Stretch && float.IsFinite(cross);
        return vertical ? new(stretch ? cross : 0, cross, minimum, maximum) : new(minimum, maximum, stretch ? cross : 0, cross);
    }
}
