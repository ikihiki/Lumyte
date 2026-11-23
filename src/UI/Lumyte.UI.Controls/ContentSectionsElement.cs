using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Measures ordinary named sections once in declaration order, within the remaining constraints.</summary>
/// <param name="widget">The passive content declaration.</param>
public class ContentSectionsElement(Widget widget) : UiElement(widget)
{
    /// <inheritdoc/>
    protected override bool HitTestSelf(Vector2 localPosition) => false;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 result = Vector2.Zero;
        foreach (UiElement child in Children)
        {
            Vector2 size = LayoutChild(child, new(0, constraints.MaxWidth, 0, Math.Max(0, constraints.MaxHeight - result.Y)));
            SetChildOffset(child, new(0, result.Y));
            result = new(Math.Max(result.X, size.X), result.Y + size.Y);
        }

        return constraints.Constrain(result);
    }
}
