using System.Numerics;
using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
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
                : base(1, int.MaxValue)
            {
            }

            /// <summary>Gets the zero or one ordinary child, initialized at most once.</summary>
            [ComposeContent]
            public WidgetChildren Children => ContentCollection;

            /// <summary>Gets an immutable snapshot of the children owned by the background slot.</summary>
            public WidgetChildren BackgroundChildren => BackgroundCollection;

            /// <inheritdoc/>
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                Vector2 contentSize = Vector2.Zero;
                foreach (Widget child in Children)
                {
                    contentSize = LayoutChild(child, constraints.Loosen());
                    SetChildOffset(child, Vector2.Zero);
                }

                Vector2 size = constraints.Constrain(contentSize);
                foreach (Widget child in BackgroundChildren)
                {
                    LayoutChild(child, UiBoxConstraints.Tight(size));
                    SetChildOffset(child, Vector2.Zero);
                }

                return size;
            }

            [ComposeSlot]
            private static void Background(Button target, Widget[] children) =>
                target.BackgroundCollection.Initialize(children);
        }
    }
}
