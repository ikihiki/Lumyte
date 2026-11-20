using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal sealed class DataGridPanel : Widget
{
    internal DataGridPanel()
    {
        Content = CreateChildren(2);
    }

    internal WidgetChildren Content { get; }

    internal required float HeaderHeight { get; init; }

    protected override UiElement CreateElement() => new PanelElement(this);

    private sealed class PanelElement(DataGridPanel widget) : UiElement<DataGridPanel>(widget)
    {
        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            float width = Widget.Width ?? 0;
            float height = constraints.HasBoundedHeight ? constraints.MaxHeight : Widget.Height ?? 240;
            float header = Math.Min(height, Widget.HeaderHeight);
            LayoutChild(Children[0], UiBoxConstraints.Tight(new(width, header)));
            SetChildOffset(Children[0], Vector2.Zero);
            LayoutChild(Children[1], UiBoxConstraints.Tight(new(width, Math.Max(0, height - header))));
            SetChildOffset(Children[1], new(0, header));
            return constraints.Constrain(new(width, height));
        }
    }
}
