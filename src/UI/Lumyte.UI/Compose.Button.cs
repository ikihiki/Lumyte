using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container with one content child and an ordered background slot.</summary>
        [Composable]
        public partial class Button : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Button"/> class.</summary>
            public Button()
            {
                Focusable = true;
                Children = CreateChildren(1);
                BackgroundChildren = CreateChildren();
            }

            /// <summary>Gets the zero or one ordinary child, initialized at most once.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets an immutable snapshot of the children owned by the background slot.</summary>
            public WidgetChildren BackgroundChildren { get; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ButtonElement(this);

            [ComposeSlot]
            private static void Background(Button target, Widget[] children) =>
                target.BackgroundChildren.Initialize(children);
        }
    }
}
