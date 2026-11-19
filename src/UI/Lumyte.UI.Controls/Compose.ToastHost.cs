using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A normal input-transparent overlay borrowing exactly one notification queue.</summary>
        [Composable]
        public partial class ToastHost : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="ToastHost"/> class.</summary>
            public ToastHost()
            {
                Role = UiRole.Group;
                Children = CreateChildren(0);
            }

            /// <summary>Gets the borrowed, owner-thread controller.</summary>
            [ComposeParameter]
            public required ToastController Controller { get; init; }

            /// <summary>Gets the empty ordinary slot; content comes from admitted notifications.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ToastHostElement(this);
        }
    }
}
