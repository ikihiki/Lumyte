using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Media;

/// <summary>Provides generated media factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive media configurations.</summary>
    public static partial class Definitions
    {
        /// <summary>Displays indeterminate progress driven only by the borrowed surface animation clock.</summary>
        [Composable]
        public partial class BusyIndicator : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="BusyIndicator"/> class.</summary>
            public BusyIndicator()
            {
                Role = UiRole.BusyIndicator;
            }

            /// <summary>Gets or sets the explicit indicator color.</summary>
            [ComposeParameter]
            public Color2D Color { get; set; } = new(0.2f, 0.4f, 0.8f, 1);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new BusyIndicatorElement(this);
        }
    }
}
