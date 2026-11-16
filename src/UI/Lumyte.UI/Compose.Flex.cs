using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Declares Flex layout without owning runtime geometry.</summary>
        [Composable]
        public partial class Flex : Panel
        {
            private UiOrientation _orientation = UiOrientation.Vertical;
            private float _spacing = 0;

            /// <summary>Initializes a new instance of the <see cref="Flex"/> class.</summary>
            public Flex()
            {
            }

            /// <summary>Gets or sets MainAxisAlignment; initially Start.</summary>
            [ComposeParameter]
            public UiMainAxisAlignment MainAxisAlignment { get; set; }

            /// <summary>Gets or sets CrossAxisAlignment; initially Start.</summary>
            [ComposeParameter]
            public UiCrossAxisAlignment CrossAxisAlignment { get; set; }

            /// <summary>Gets or sets the main axis, initially vertical; undefined values are rejected on adoption.</summary>
            [ComposeParameter]
            public UiOrientation Orientation
            {
                get => _orientation;
                set => _orientation = value;
            }

            /// <summary>Gets or sets the finite nonnegative gap between visible children, initially zero.</summary>
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
            protected override UiElement CreateElement() => new FlexElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => C4Layout.Validate(this, context);
        }
    }
}
