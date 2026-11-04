using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    internal bool NeedsHostValidation => RequiresHostValidation;

    /// <summary>Gets a value indicating whether the host must validate this component's borrowed services.</summary>
    protected virtual bool RequiresHostValidation => false;

    internal bool HostRequirementsChanged(WidgetDefinition previous, WidgetDefinition next) => HostServicesChanged(previous, next);

    /// <summary>Checks changes to service requirements independently of visual and layout changes.</summary>
    /// <param name="previous">The previous configuration.</param>
    /// <param name="next">The new configuration.</param>
    /// <returns>Whether host service validation must run again.</returns>
    protected virtual bool HostServicesChanged(WidgetDefinition previous, WidgetDefinition next) => !ReferenceEquals(previous, next);
}
