using System.Numerics;
using StackWidget = Lumyte.UI.Compose.Definitions.Stack;

namespace Lumyte.UI;

/// <summary>Implements persistent stack layout independently of replaceable declarations.</summary>
public partial class StackElement : UiElement<StackWidget>
{
    /// <summary>Initializes a new instance of the <see cref="StackElement"/> class.</summary>
    /// <param name="widget">The initial configuration.</param>
    protected internal StackElement(StackWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(StackWidget previous, StackWidget next) => previous.Orientation == next.Orientation && previous.Spacing == next.Spacing ? UiInvalidation.None : UiInvalidation.Layout;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        bool vertical = Widget.Orientation == UiOrientation.Vertical;
        UiBoxConstraints childConstraints = vertical
            ? new(0, constraints.MaxWidth, 0, float.PositiveInfinity)
            : new(0, float.PositiveInfinity, 0, constraints.MaxHeight);
        double mainSize = 0;
        double crossSize = 0;
        bool hasVisibleChild = false;
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            Vector2 size = LayoutChild(child, childConstraints);
            if (!child.Widget.IsVisible)
            {
                SetChildOffset(child, Vector2.Zero);
                continue;
            }

            if (hasVisibleChild)
            {
                mainSize += Widget.Spacing;
            }

            float offset = UiLayoutMath.FiniteFloat(mainSize);
            SetChildOffset(child, vertical ? new(0, offset) : new(offset, 0));
            mainSize += vertical ? size.Y : size.X;
            crossSize = Math.Max(crossSize, vertical ? size.X : size.Y);
            hasVisibleChild = true;
        }

        return vertical
            ? UiLayoutMath.FillBounded(constraints, crossSize, mainSize)
            : UiLayoutMath.FillBounded(constraints, mainSize, crossSize);
    }
}
