using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Provides static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Builds a reactive subtree, automatically tracking its direct signal and computed reads.</summary>
    /// <typeparam name="TWidget">The concrete subtree declaration type.</typeparam>
    /// <param name="build">Creates a fresh subtree configuration; cached child declarations may be reused.</param>
    /// <returns>The subtree declaration with an independent reconstruction recipe.</returns>
    public static TWidget Compute<TWidget>(Func<TWidget> build)
        where TWidget : Widget => (TWidget)UiSurface.CaptureBuild(build);
}
