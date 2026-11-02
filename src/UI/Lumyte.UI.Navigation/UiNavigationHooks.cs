using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation;

/// <summary>Connects optional navigation to function component context.</summary>
public static class UiNavigationHooks
{
    private static readonly UiContextKey<IUiNavigator> _navigation = new();

    /// <summary>Gets the nearest navigator provided to the current function subtree.</summary>
    /// <returns>The borrowed navigator.</returns>
    public static IUiNavigator UseNavigation() => Compose.UseContext(_navigation);

    /// <summary>Provides navigation to a standalone fresh declaration or overrides it for a subtree.</summary>
    /// <param name="navigation">The borrowed navigator.</param>
    /// <param name="content">The fresh declaration subtree.</param>
    /// <returns>The same declaration with navigation context.</returns>
    public static Widget ProvideNavigation(IUiNavigator navigation, Widget content)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return Compose.Provide(_navigation, navigation, content);
    }
}
