using System.Numerics;
using WrapWidget = Lumyte.UI.Compose.Definitions.Wrap;

namespace Lumyte.UI;

internal sealed class WrapElement(WrapWidget widget) : UiElement<WrapWidget>(widget)
{
    protected override UiInvalidation CompareConfiguration(WrapWidget previous, WrapWidget next) => previous.Orientation == next.Orientation && previous.Spacing == next.Spacing && previous.RunSpacing == next.RunSpacing ? UiInvalidation.None : UiInvalidation.Layout;

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        bool vertical = Widget.Orientation == UiOrientation.Vertical;
        double maximum = vertical ? constraints.MaxHeight : constraints.MaxWidth;
        double main = 0;
        double cross = 0;
        double runCross = 0;
        double widest = 0;
        bool hasChild = false;
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            Vector2 size = LayoutChild(child, constraints.Loosen());
            if (!child.LayoutVisible)
            {
                SetChildOffset(child, Vector2.Zero);
                continue;
            }

            double length = C4Layout.Main(size, vertical);
            double gap = hasChild ? Widget.Spacing : 0;
            if (hasChild && main + gap + length > maximum)
            {
                widest = Math.Max(widest, main);
                cross += runCross + Widget.RunSpacing;
                main = 0;
                runCross = 0;
                gap = 0;
            }

            main += gap;
            SetChildOffset(child, C4Layout.Vector(main, cross, vertical));
            main += length;
            runCross = Math.Max(runCross, C4Layout.Cross(size, vertical));
            hasChild = true;
        }

        widest = Math.Max(widest, main);
        return vertical ? UiLayoutMath.FillBounded(constraints, cross + runCross, widest) : UiLayoutMath.FillBounded(constraints, widest, cross + runCross);
    }
}
