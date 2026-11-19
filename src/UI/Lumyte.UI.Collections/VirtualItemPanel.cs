using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal sealed class VirtualItemPanel : Widget
{
    internal VirtualItemPanel()
    {
        Content = CreateChildren();
    }

    internal WidgetChildren Content { get; }

    internal required UiOrientation Orientation { get; init; }

    internal required float Extent { get; init; }

    internal required float ItemExtent { get; init; }

    internal required int[] Indices { get; init; }

    protected override UiElement CreateElement() => new PanelElement(this);

    private sealed class PanelElement(VirtualItemPanel widget) : UiElement<VirtualItemPanel>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            bool vertical = Widget.Orientation == UiOrientation.Vertical;
            float cross = vertical ? constraints.MaxWidth : constraints.MaxHeight;
            for (int index = 0; index < Children.Count; index++)
            {
                UiElement child = Children[index];
                LayoutChild(child, UiBoxConstraints.Tight(vertical ? new(cross, Widget.ItemExtent) : new(Widget.ItemExtent, cross)));
                float position = (float)((double)Widget.Indices[index] * Widget.ItemExtent);
                SetChildOffset(child, vertical ? new(0, position) : new(position, 0));
            }

            return constraints.Constrain(vertical ? new(cross, Widget.Extent) : new(Widget.Extent, cross));
        }
    }
}
