using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Source-generated function declarations used by ownership regressions.</summary>
public static partial class OwnershipFunctions
{
    /// <summary>Contains construction functions.</summary>
    public static partial class Definitions
    {
        /// <summary>A reactive custom declaration that replaces its own element type.</summary>
        [Composable(Factory = "OwnershipFunctions")]
        public partial class Replacing : Widget
        {
            /// <summary>Gets or sets a value indicating whether the reactive argument requests recreation.</summary>
            [ComposeParameter]
            public bool Change { get; set; }

            /// <summary>Gets or sets the next declaration factory.</summary>
            [ComposeParameter]
            public Func<Widget>? Next { get; set; }

            /// <inheritdoc/>
            protected override Widget? RebuildDeclaration(UiWidgetRebuildContext context) => Next!();
        }
    }
}
