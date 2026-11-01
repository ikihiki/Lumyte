using System.Numerics;
using PanelWidget = Lumyte.UI.Compose.Definitions.Panel;

namespace Lumyte.UI;

/// <summary>Implements persistent panel layout independently of replaceable declarations.</summary>
public partial class PanelElement : UiElement<PanelWidget>
{
    /// <summary>Initializes a new instance of the <see cref="PanelElement"/> class.</summary>
    /// <param name="widget">The initial configuration.</param>
    protected internal PanelElement(PanelWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(PanelWidget previous, PanelWidget next) => UiInvalidation.None;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 contentSize = Vector2.Zero;
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            Vector2 size = LayoutChild(child, constraints.Loosen());
            contentSize = Vector2.Max(contentSize, size);
            SetChildOffset(child, Vector2.Zero);
        }

        return UiLayoutMath.FillBounded(constraints, contentSize.X, contentSize.Y);
    }
}
