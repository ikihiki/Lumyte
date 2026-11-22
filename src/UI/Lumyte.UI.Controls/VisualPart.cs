using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

internal sealed class VisualPart : Widget
{
    internal VisualPart(string key, Widget content, Func<Vector2, (Vector2 Size, Vector2 Offset)> geometry)
    {
        Key = key;
        Content = content;
        Geometry = geometry;
    }

    internal Widget Content { get; }

    internal Func<Vector2, (Vector2 Size, Vector2 Offset)> Geometry { get; }

    protected override UiElement CreateElement() => new PartElement(this);

    private sealed class PartElement(VisualPart widget) : UiElement<VisualPart>(widget)
    {
        protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
        {
            var proposed = (VisualPart)next;

            // A fixed source identifies this boundary, not the current reactive configuration.
            // Only this still-mounted part may retain its descendant's reconstructed declaration.
            Widget content = ReferenceEquals(proposed.Content, Widget.Content) && IsMounted && Children.Count == 1
                ? Children[0].Widget : proposed.Content;
            return new(content, ownsContent: true);
        }

        protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
        {
            Vector2 size = constraints.Constrain(new(constraints.MaxWidth, constraints.MaxHeight));
            foreach (UiElement child in Children)
            {
                LayoutChild(child, UiBoxConstraints.Tight(size));
                SetChildOffset(child, Vector2.Zero);
            }

            return size;
        }
    }
}
