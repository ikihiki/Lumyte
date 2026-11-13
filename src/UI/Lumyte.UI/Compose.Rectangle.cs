using Lumyte.Composition;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Fills its final local bounds with a straight-alpha sRGB color.</summary>
        [Composable]
        public partial class Rectangle : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Rectangle"/> class.</summary>
            public Rectangle()
            {
            }

            /// <summary>Gets or sets the fill, whose components are validated as unit straight sRGB before assignment.</summary>
            [ComposeParameter]
            public Rgba<float> Fill { get; set => field = UiColorPolicy.Unit(value, nameof(Fill)); }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new RectangleElement(this);
        }
    }
}
