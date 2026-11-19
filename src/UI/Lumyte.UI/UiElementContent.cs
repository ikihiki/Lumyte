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
        : this(content, false, onCommitted)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UiElementContent"/> class with an optional exclusive ownership boundary.</summary>
    /// <param name="content">The proposed passive root.</param>
    /// <param name="ownsContent">Whether the root must be unparented and its subtree exclusive to this host.</param>
    /// <param name="onCommitted">An optional publication action.</param>
    public UiElementContent(Widget content, bool ownsContent, Action? onCommitted = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (ownsContent && content.HasDeclarationParent)
        {
            throw new InvalidOperationException("Owned templates must return an unparented root.");
        }

        CollectionItemRoots = ownsContent ? Array.AsReadOnly(new[] { content }) : null;
        FreshRealizations = ownsContent;
        Content = content;
        Commit = onCommitted;
    }

    /// <summary>Gets the proposed content configuration; its mounted instance belongs to the element.</summary>
    public Widget Content { get; }

    internal IReadOnlyList<Widget>? CollectionItemRoots { get; init; }

    internal bool RequiresFreshDescendants { get; init; }

    internal bool FreshRealizations { get; set; }

    internal Action? PrepareLayout { get; set; }

    internal object? PreparationState { get; set; }

    internal Action? Commit { get; }

    internal SignalReadSet? Reads { get; init; }
}
