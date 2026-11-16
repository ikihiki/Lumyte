using System.Numerics;
using WrapWidget = Lumyte.UI.Compose.Definitions.Wrap;

namespace Lumyte.UI;

internal sealed class WrapElement(WrapWidget widget) : UiElement<WrapWidget>(widget)
{
    protected override UiInvalidation CompareConfiguration(WrapWidget previous, WrapWidget next) => previous.Orientation == next.Orientation && previous.Spacing == next.Spacing && previous.RunSpacing == next.RunSpacing && previous.MainAxisAlignment == next.MainAxisAlignment && previous.CrossAxisAlignment == next.CrossAxisAlignment && previous.RunAlignment == next.RunAlignment ? UiInvalidation.None : UiInvalidation.Layout;

    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        bool vertical = Widget.Orientation == UiOrientation.Vertical;
        double maximum = vertical ? constraints.MaxHeight : constraints.MaxWidth;
        var runs = new List<(List<UiElement> Items, double Main, double Cross)>();
        var items = new List<UiElement>();
        double main = 0;
        double runCross = 0;
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            Vector2 size = LayoutChild(child, constraints.Loosen());
            if (!child.LayoutVisible)
            {
                SetChildOffset(child, Vector2.Zero);
                continue;
            }

            double length = C4Layout.Main(size, vertical);
            double gap = items.Count > 0 ? Widget.Spacing : 0;
            if (items.Count > 0 && main + gap + length > maximum)
            {
                runs.Add((items, main, runCross));
                items = [];
                main = 0;
                runCross = 0;
                gap = 0;
            }

            items.Add(child);
            main += gap + length;
            runCross = Math.Max(runCross, C4Layout.Cross(size, vertical));
        }

        if (items.Count > 0)
        {
            runs.Add((items, main, runCross));
        }

        double widest = runs.Count == 0 ? 0 : runs.Max(run => run.Main);
        double cross = runs.Sum(run => run.Cross) + ((double)Math.Max(0, runs.Count - 1) * Widget.RunSpacing);
        Vector2 result = vertical ? UiLayoutMath.FillBounded(constraints, cross, widest) : UiLayoutMath.FillBounded(constraints, widest, cross);
        (double crossOffset, double runGap) = C4Layout.Distribution(Widget.RunAlignment, C4Layout.Cross(result, vertical) - cross, runs.Count);
        foreach ((List<UiElement> Items, double Main, double Cross) run in runs)
        {
            (double offset, double extraGap) = C4Layout.Distribution(Widget.MainAxisAlignment, C4Layout.Main(result, vertical) - run.Main, run.Items.Count);
            foreach (UiElement child in run.Items)
            {
                SetChildOffset(child, C4Layout.Vector(offset, crossOffset + C4Layout.CrossOffset(Widget.CrossAxisAlignment, run.Cross - C4Layout.Cross(child.Size, vertical)), vertical));
                offset += C4Layout.Main(child.Size, vertical) + Widget.Spacing + extraGap;
            }

            crossOffset += run.Cross + Widget.RunSpacing + runGap;
        }

        return result;
    }
}
