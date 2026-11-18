using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections.Tests.SlotRecipes;

/// <summary>Provides a generated declaration that forces root recipe reconstruction.</summary>
public static partial class Compose
{
    /// <summary>Contains the root recipe fixture.</summary>
    public static partial class Definitions
    {
        /// <summary>Uses its generated factory only through the enclosing root recipe.</summary>
        [Composable]
        public partial class RecipeTrigger : Lumyte.UI.Compose.Definitions.SizedBox
        {
            /// <inheritdoc/>
            protected override Widget? RebuildDeclaration(UiWidgetRebuildContext context) => null;
        }
    }
}
