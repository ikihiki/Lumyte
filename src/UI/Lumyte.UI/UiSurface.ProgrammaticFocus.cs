using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    /// <summary>Try Focus.</summary>
    /// <param name = "widget">The widget.</param>
    /// <returns>The operation result.</returns>
    public bool TryFocus(Widget? widget)
    {
        ThrowIfDisposed();
        if (widget is not null && !ReferenceEquals(widget.Surface, this))
        {
            throw new ArgumentException("The widget does not belong to this surface.", nameof(widget));
        }

        if (widget is not null && !IsFocusEligible(widget))
        {
            return false;
        }

        return ChangeFocus(widget);
    }
}
