using Lumyte.Composition;
using Lumyte.Mathematics.Colors;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides passive control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains control declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A single control borrowing a viewport controller.</summary>
        [Composable]
        public partial class ScrollBar : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="ScrollBar"/> class.</summary>
            public ScrollBar()
            {
                Role = UiRole.ScrollBar;
                Focusable = true;
            }

            /// <summary>Gets or sets the borrowed controller.</summary>
            [ComposeParameter]
            public ScrollController? Controller { get; set; }

            /// <summary>Gets or sets the control axis.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; }

            /// <summary>Gets or sets the finite nonnegative minimum thumb length.</summary>
            [ComposeParameter]
            public float MinimumThumbSize { get; set; } = 16;

            /// <summary>Gets or sets the finite positive arrow increment.</summary>
            [ComposeParameter]
            public float LineStep { get; set; } = 20;

            /// <summary>Gets or sets the thumb color.</summary>
            [ComposeParameter]
            public SRgba<float> ThumbColor { get; set => field = ControlStyle.Validate(value); } = new(0.7f, 0.7f, 0.7f, 1);

            /// <summary>Gets or sets the track color.</summary>
            [ComposeParameter]
            public SRgba<float> TrackColor { get; set => field = ControlStyle.Validate(value); } = new(0.15f, 0.15f, 0.15f, 1);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ScrollBarElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                ArgumentNullException.ThrowIfNull(Controller);
                ObjectDisposedException.ThrowIf(Controller.IsDisposed, Controller);

                if (!Enum.IsDefined(Orientation) || !float.IsFinite(MinimumThumbSize) || MinimumThumbSize < 0 || !float.IsFinite(LineStep) || LineStep <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MinimumThumbSize));
                }
            }
        }
    }
}
