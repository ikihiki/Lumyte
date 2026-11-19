using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Source-generated function declarations used by ownership regressions.</summary>
public static partial class OwnershipFunctions
{
    /// <summary>Contains construction functions.</summary>
    public static partial class Definitions
    {
        /// <summary>Reads the nearest provider while a virtual item function is prepared.</summary>
        /// <param name="context">The explicitly provided context key.</param>
        /// <returns>The context-bearing item content.</returns>
        [Composable(Factory = "OwnershipFunctions")]
        public static Widget ReadContext(UiContextKey<string> context) => Lumyte.UI.Compose.SizedBox(automationName: Lumyte.UI.Compose.UseContext(context), height: 20);

        /// <summary>Evaluates an application factory during actual subtree preparation.</summary>
        /// <param name="build">The body factory.</param>
        /// <returns>The generated content.</returns>
        [Composable(Factory = "OwnershipFunctions")]
        public static Widget Generated(Func<Widget> build) => build();
    }
}
