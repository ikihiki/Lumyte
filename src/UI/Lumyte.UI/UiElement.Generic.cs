using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A persistent instance with strongly typed access to its current configuration.</summary>
/// <typeparam name="TWidget">The accepted declaration type.</typeparam>
public class UiElement<TWidget> : UiElement
    where TWidget : WidgetDefinition
{
    /// <summary>Initializes a new instance of the <see cref="UiElement{TWidget}"/> class.</summary>
    /// <param name="widget">The initial configuration.</param>
    protected UiElement(TWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets the current adopted configuration.</summary>
    public new TWidget Widget => (TWidget)base.Widget;

    /// <inheritdoc/>
    protected sealed override Action? PrepareUpdate(WidgetDefinition next, UiElementUpdateContext context) => PrepareUpdate((TWidget)next, context);

    /// <inheritdoc/>
    protected sealed override void OnWidgetUpdated(WidgetDefinition previous) => OnWidgetUpdated((TWidget)previous);

    /// <summary>Prepares custom state and child-reference changes for the next configuration.</summary>
    /// <param name="next">The typed proposed configuration.</param>
    /// <param name="context">The read-only proposed child topology.</param>
    /// <returns>An optional synchronous publication action.</returns>
    protected virtual Action? PrepareUpdate(TWidget next, UiElementUpdateContext context) => null;

    /// <summary>Observes replacement of the configuration without copying persistent fields.</summary>
    /// <param name="previous">The previous typed configuration.</param>
    protected virtual void OnWidgetUpdated(TWidget previous)
    {
    }
}
