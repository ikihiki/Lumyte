using Lumyte.UI;

namespace Lumyte.Maps.UI;

/// <summary>A passive declaration borrowing an explicitly owned map session.</summary>
public sealed class MapView : Compose.Definitions.Widget
{
    /// <summary>Gets or initializes the borrowed session. A mounted element cannot replace it.</summary>
    public required MapSession Session { get; init; }

    /// <inheritdoc/>
    protected override UiElement CreateElement()
    {
        ArgumentNullException.ThrowIfNull(Session);
        return new MapElement(this);
    }

    /// <inheritdoc/>
    protected override void ValidateDeclaration(UiWidgetValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(Session);
        base.ValidateDeclaration(context);
    }
}
