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
        /// <summary>A passive Switch declaration.</summary>
        [Composable]
        public partial class Switch : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Switch"/> class.</summary>
            public Switch()
            {
                Role = UiRole.Switch;
                Focusable = true;
                Children = CreateChildren(1);
                BackgroundChildren = CreateChildren();
            }

            /// <summary>Gets ordinary content, initialized once.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets the optional background slot.</summary>
            public WidgetChildren BackgroundChildren { get; }

            /// <summary>Gets or sets a value indicating whether the declared control is checked, without notification.</summary>
            [ComposeParameter]
            public bool IsOn { get; set; }

            /// <summary>Gets or sets the latest user-change callback.</summary>
            [ComposeParameter]
            public Action<bool>? OnChanged { get; set; }

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
            protected override UiElement CreateElement() => new SwitchElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!float.IsFinite(MarkSize) || MarkSize < 0 || !float.IsFinite(Gap) || Gap < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MarkSize));
                }
            }

            [ComposeSlot]
            private static void Background(Switch target, Widget[] children) => target.BackgroundChildren.Initialize(children);
        }
    }
}
