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
            {
                ContentCollection = new ChildCollection(this, 1);
                BackgroundCollection = new ChildCollection(this, int.MaxValue);
            }

            /// <summary>Gets or sets the zero or one content child using an immutable snapshot.</summary>
            [ComposeContent]
            public IReadOnlyList<Widget> Children
            {
                get => ContentCollection!.Items;
                set => ContentCollection!.Replace(value);
            }

            /// <summary>Gets an immutable snapshot of the children owned by the background slot.</summary>
            public IReadOnlyList<Widget> BackgroundChildren => BackgroundCollection!.Items;

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
            private static void Background(Button target, IReadOnlyList<Widget> children) =>
                target.BackgroundCollection!.Replace(children);
        }
    }
}
