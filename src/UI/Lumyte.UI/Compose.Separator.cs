using Lumyte.Composition;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains declarations consumed by Composition.</summary>
    public static partial class Definitions
    {
        /// <summary>A noninteractive straight decorative line.</summary>
        [Composable]
        public partial class Separator : Widget
        {
            /// <summary>Gets or sets the line direction.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; } = UiOrientation.Horizontal;

            /// <summary>Gets or sets the finite, nonnegative requested thickness.</summary>
            [ComposeParameter]
            public float Thickness { get; set; } = 1;

            /// <summary>Gets or sets the line color; the default is transparent.</summary>
            [ComposeParameter]
            public Rgba<float> Color { get; set => field = UiColorPolicy.Unit(value, nameof(Color)); }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new SeparatorElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                UiLayoutMath.ValidateDimension(Thickness, nameof(Thickness));
                if (Orientation is not (UiOrientation.Horizontal or UiOrientation.Vertical))
                {
                    throw new ArgumentOutOfRangeException(nameof(Orientation));
                }
            }
        }
    }
}
