using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Media;

/// <summary>Provides generated media factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive media configurations.</summary>
    public static partial class Definitions
    {
        /// <summary>A read-only finite normalized progress value, without callbacks or activation.</summary>
        [Composable]
        public partial class ProgressBar : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="ProgressBar"/> class.</summary>
            public ProgressBar()
            {
                Role = UiRole.ProgressBar;
            }

            /// <summary>Gets or sets a finite value in the closed interval [0,1].</summary>
            [ComposeParameter]
            public double Value { get; set; }

            /// <summary>Gets or sets the explicit foreground color.</summary>
            [ComposeParameter]
            public Color2D Foreground { get; set; } = new(0.2f, 0.4f, 0.8f, 1);

            /// <summary>Gets or sets the explicit track color.</summary>
            [ComposeParameter]
            public Color2D Background { get; set; } = new(0.2f, 0.2f, 0.2f, 1);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ProgressBarElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!double.IsFinite(Value) || Value < 0 || Value > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(Value));
                }
            }
        }
    }
}
