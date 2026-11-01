using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using RectangleWidget = Lumyte.UI.Compose.Definitions.Rectangle;

namespace Lumyte.UI;

/// <summary>Records a rectangle from the current configuration and persistent geometry.</summary>
public partial class RectangleElement : UiElement<RectangleWidget>
{
    /// <summary>Initializes a new instance of the <see cref="RectangleElement"/> class.</summary>
    /// <param name="widget">The initial configuration.</param>
    protected internal RectangleElement(RectangleWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(RectangleWidget previous, RectangleWidget next) =>
        previous.Fill == next.Fill ? UiInvalidation.None : UiInvalidation.Visual;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => UiLayoutMath.FillBounded(constraints, 0, 0);

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (Size.X > 0 && Size.Y > 0 && Widget.Fill.A > 0)
        {
            canvas.FillRect(new(0, 0, Size.X, Size.Y), Widget.Fill);
        }
    }
}
