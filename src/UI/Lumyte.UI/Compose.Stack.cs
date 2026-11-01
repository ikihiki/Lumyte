using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Positions children sequentially without shrinking them to the remaining space.</summary>
        [Composable]
        public partial class Stack : Panel
        {
            private float _spacing;

            /// <summary>Initializes a new instance of the <see cref="Stack"/> class.</summary>
            public Stack()
            {
            }

            /// <summary>Gets or sets the main axis, which defaults to vertical.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; }

            /// <summary>Gets or sets the finite nonnegative gap between visible children.</summary>
            [ComposeParameter]
            public float Spacing
            {
                get => _spacing;
                set
                {
                    UiLayoutMath.ValidateDimension(value, nameof(value));
                    _spacing = value;
                }
            }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new StackElement(this);
        }
    }
}
