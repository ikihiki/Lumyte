using Lumyte.Composition;
using Lumyte.Mathematics.Colors;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides generated typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive control declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive RadioButton declaration.</summary>
        [Composable]
        public partial class RadioButton : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="RadioButton"/> class.</summary>
            public RadioButton()
            {
                Role = UiRole.RadioButton;
                Focusable = true;
                Children = CreateChildren(1);
                BackgroundChildren = CreateChildren();
            }

            /// <summary>Gets ordinary content, initialized once.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets the optional background slot.</summary>
            public WidgetChildren BackgroundChildren { get; }

            /// <summary>Gets or sets the finite nonnegative mark size.</summary>
            [ComposeParameter]
            public float MarkSize { get; set; } = 18;

            /// <summary>Gets or sets the finite nonnegative content gap.</summary>
            [ComposeParameter]
            public float Gap { get; set; } = 6;

            /// <summary>Gets or sets the explicit mark foreground.</summary>
            [ComposeParameter]
            public SRgba<float> Foreground { get; set => field = ControlStyle.Validate(value); } = new(1, 1, 1, 1);

            /// <summary>Gets or sets the explicit mark track.</summary>
            [ComposeParameter]
            public SRgba<float> TrackColor { get; set => field = ControlStyle.Validate(value); } = new(0.2f, 0.3f, 0.5f, 1);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new RadioButtonElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!float.IsFinite(MarkSize) || MarkSize < 0 || !float.IsFinite(Gap) || Gap < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MarkSize));
                }

                if (string.IsNullOrEmpty(Key) || context.GetParent(this) is not RadioGroup)
                {
                    throw new InvalidOperationException("RadioButton requires a nonempty inherited Key and direct RadioGroup parent.");
                }
            }

            [ComposeSlot]
            private static void Background(RadioButton target, Widget[] children) => target.BackgroundChildren.Initialize(children);
        }
    }
}
