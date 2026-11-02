using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private UiVisualBudget? _visualBudget;

    internal UiMotionOptions Motion => _options.Motion;

    internal UiVisualBudget VisualBudget => _visualBudget ??= new(Motion);

    internal UiVisualCoordinator AttachVisuals(UiSurface surface, Func<UiWindowId?> window)
    {
        var coordinator = new UiVisualCoordinator(this, surface, window);
        surface.VisualHost = coordinator;
        coordinator.Publish();
        return coordinator;
    }
}
