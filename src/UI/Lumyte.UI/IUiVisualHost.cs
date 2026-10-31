using Lumyte.Graphics.Drawing2D;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Internal retained-layer bridge for the standard compositor.</summary>
internal interface IUiVisualHost
{
    /// <summary>Gets a value indicating whether this declaration has been published.</summary>
    bool CanPresent { get; }

    /// <summary>Gets the validated immutable motion policy.</summary>
    UiMotionOptions Motion { get; }

    /// <summary>Records retained element content with its current presentation pose.</summary>
    /// <param name="widget">The connected element.</param>
    /// <param name="canvas">The local canvas.</param>
    /// <returns>Whether normal drawing was replaced.</returns>
    bool Record(Widget widget, DrawingCanvas2D canvas);
}
