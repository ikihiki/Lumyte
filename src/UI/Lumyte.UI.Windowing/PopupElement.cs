using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Windowing;

/// <summary>Represents a zero-size portal on its owner surface.</summary>
public class PopupElement : UiElement<Compose.Definitions.Popup>
{
    /// <summary>Initializes a new instance of the <see cref="PopupElement"/> class.</summary>
    /// <param name="widget">The passive portal declaration.</param>
    protected internal PopupElement(Compose.Definitions.Popup widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Vector2.Zero);

    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
    }
}
