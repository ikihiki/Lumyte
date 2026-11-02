using System.Numerics;
using ButtonWidget = Lumyte.UI.Compose.Definitions.Button;

namespace Lumyte.UI;

/// <summary>Implements persistent button layout independently of replaceable declarations.</summary>
public partial class ButtonElement : UiElement<ButtonWidget>
{
    /// <summary>Initializes a new instance of the <see cref="ButtonElement"/> class.</summary>
    /// <param name="widget">The initial configuration.</param>
    protected internal ButtonElement(ButtonWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => Widget.GetType() == typeof(ButtonWidget);

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(Compose.Definitions.Widget other) => other.GetType() == typeof(ButtonWidget);

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(ButtonWidget previous, ButtonWidget next) => UiInvalidation.None;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 contentSize = Vector2.Zero;
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            contentSize = LayoutChild(child, constraints.Loosen());
            SetChildOffset(child, Vector2.Zero);
        }

        Vector2 size = constraints.Constrain(contentSize);
        foreach (UiElement child in GetChildren(Widget.BackgroundChildren))
        {
            LayoutChild(child, UiBoxConstraints.Tight(size));
            SetChildOffset(child, Vector2.Zero);
        }

        return size;
    }
}
