using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Declares Wrap layout without owning runtime geometry.</summary>
        [Composable]
        public partial class Wrap : Panel
        {
            private UiOrientation _orientation = UiOrientation.Horizontal;
            private float _spacing = 0;
            private float _runSpacing = 0;

            /// <summary>Initializes a new instance of the <see cref="Wrap"/> class.</summary>
            public Wrap()
            {
            }

            /// <summary>Gets or sets MainAxisAlignment; initially Start.</summary>
            [ComposeParameter]
            public UiMainAxisAlignment MainAxisAlignment { get; set; }

            /// <summary>Gets or sets CrossAxisAlignment; initially Start.</summary>
            [ComposeParameter]
            public UiCrossAxisAlignment CrossAxisAlignment { get; set; }

            /// <summary>Gets or sets RunAlignment; initially Start.</summary>
            [ComposeParameter]
            public UiMainAxisAlignment RunAlignment { get; set; }

            /// <summary>Gets or sets the main axis, initially horizontal; undefined values are rejected on adoption.</summary>
            [ComposeParameter]
            public UiOrientation Orientation
            {
                get => _orientation;
                set => _orientation = value;
            }

            /// <summary>Gets or sets the finite nonnegative gap within each run, initially zero.</summary>
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

            /// <summary>Gets or sets the finite nonnegative gap between runs, initially zero.</summary>
            [ComposeParameter]
            public float RunSpacing
            {
                get => _runSpacing;
                set
                {
                    UiLayoutMath.ValidateDimension(value, nameof(value));
                    _runSpacing = value;
                }
            }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new WrapElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => C4Layout.Validate(this, context);
        }
    }
}
