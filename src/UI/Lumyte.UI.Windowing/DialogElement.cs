using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Windowing;

/// <summary>A zero-sized portal; content never becomes a child of the declaring surface.</summary>
public class DialogElement : UiElement<Compose.Definitions.Dialog>
{
    /// <summary>Initializes a new instance of the <see cref="DialogElement"/> class.</summary>
    /// <param name="widget">The passive declaration.</param>
    protected internal DialogElement(Compose.Definitions.Dialog widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Vector2.Zero);

    /// <inheritdoc/>
    protected override bool HitTestSelf(Vector2 localPosition) => false;

    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
    }
}
