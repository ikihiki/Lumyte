using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container owning an ordered list of ordinary children.</summary>
        [Composable]
        public partial class Panel : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Panel"/> class.</summary>
            public Panel()
            {
                Role = UiRole.Group;
                Children = CreateChildren();
            }

            /// <summary>Gets the ordinary children, initialized at most once.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new PanelElement(this);
        }
    }
}
