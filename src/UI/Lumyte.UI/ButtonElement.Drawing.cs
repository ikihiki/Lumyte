using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Implements persistent button behavior independently of replaceable declarations.</summary>
public partial class ButtonElement
{
    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
        foreach (UiElement child in GetChildren(Widget.BackgroundChildren))
        {
            RecordChild(child, canvas);
        }

        RecordSelf(canvas);
        foreach (UiElement child in GetChildren(Widget.Children))
        {
            RecordChild(child, canvas);
        }
    }
}
