using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Declares Expanded layout without owning runtime geometry.</summary>
        [Composable]
        public partial class Expanded : SizedBox
        {
            private float _weight = 1;

            /// <summary>Initializes a new instance of the <see cref="Expanded"/> class.</summary>
            public Expanded()
            {
            }

            /// <summary>Gets or sets the finite positive allocation weight, initially one.</summary>
            [ComposeParameter]
            public float Weight
            {
                get => _weight;
                set
                {
                    UiLayoutMath.ValidateDimension(value, nameof(value));
                    ArgumentOutOfRangeException.ThrowIfZero(value);
                    _weight = value;
                }
            }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new C4BoxElement<Expanded>(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => C4Layout.Validate(this, context);
        }
    }
}
