using Lumyte.Signals;
using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    internal UiUseEnvironment? Contexts { get; set; }

    internal SignalReadSet? ContentReads { get; set; }

    internal UiElementContent? PrepareContent(WidgetDefinition next, UiElementBuildContext context) => BuildContent(next, context);

    /// <summary>Optionally builds an instance-owned content slot before proposed-tree matching and validation.</summary>
    /// <param name="next">The proposed configuration with resolved construction arguments.</param>
    /// <param name="context">The proposed construction environment.</param>
    /// <returns>Prepared content and a publication action, or null to use declaration child slots.</returns>
    protected virtual UiElementContent? BuildContent(WidgetDefinition next, UiElementBuildContext context) => null;
}
