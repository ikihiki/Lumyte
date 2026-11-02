using Lumyte.Composition;
using Lumyte.UI.Navigation;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Navigation.UiNavigationHooks;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Defines routed functions that obtain navigation through the shared hook frame.</summary>
public static partial class NavigationFunctionUi
{
    /// <summary>Contains function component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Reads navigation without a context or navigation parameter.</summary>
        /// <param name="created">Observes the borrowed navigator and button callback.</param>
        /// <returns>A button whose event initiates navigation.</returns>
        [Composable(Factory = "NavigationFunctionUi")]
        public static Widget Link(Action<IUiNavigator, Action> created)
        {
            IUiNavigator navigation = UseNavigation();
            Action click = () => navigation.RequestNavigate("/details");
            created(navigation, click);
            return Button(testId: "navigate", onClick: click);
        }

        /// <summary>Returns another function component without forwarding services.</summary>
        /// <param name="created">Observes the child.</param>
        /// <returns>The generated child declaration.</returns>
        [Composable(Factory = "NavigationFunctionUi")]
        public static Widget Parent(Action<IUiNavigator, Action> created) => NavigationFunctionUi.Link(created);
    }
}
