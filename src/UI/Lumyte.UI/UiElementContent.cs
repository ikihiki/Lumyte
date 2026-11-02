using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A proposed child declaration owned by a persistent component instance.</summary>
public sealed class UiElementContent
{
    /// <summary>Initializes a new instance of the <see cref="UiElementContent"/> class.</summary>
    /// <param name="content">The proposed passive content declaration.</param>
    /// <param name="onCommitted">An optional action adopting prepared local state after publication.</param>
    public UiElementContent(Widget content, Action? onCommitted = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        Content = content;
        Commit = onCommitted;
    }

    /// <summary>Gets the proposed content configuration; its mounted instance belongs to the element.</summary>
    public Widget Content { get; }

    internal Action? Commit { get; }

    internal SignalReadSet? Reads { get; init; }
}
