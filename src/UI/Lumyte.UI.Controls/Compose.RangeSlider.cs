using Lumyte.Composition;
using Lumyte.Mathematics.Colors;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive RangeSlider declaration with per-surface accepted state.</summary>
        [Composable]
        public partial class RangeSlider : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="RangeSlider"/> class.</summary>
            public RangeSlider()
            {
                Focusable = true;
                Role = UiRole.RangeSlider;
            }

            /// <summary>Gets or sets the finite lower bound.</summary>
            [ComposeParameter]
            public double Min { get; set; }

            /// <summary>Gets or sets the finite upper bound, at least Min.</summary>
            [ComposeParameter]
            public double Max { get; set; } = 1;

            /// <summary>Gets or sets the finite positive lattice step.</summary>
            [ComposeParameter]
            public double Step { get; set; } = 0.01;

            /// <summary>Gets or sets the axis; vertical increases downward.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; } = UiOrientation.Horizontal;

            /// <summary>Gets or sets a value indicating whether the direction is reversed.</summary>
            [ComposeParameter]
            public bool IsReversed { get; set; }

            /// <summary>Gets or sets the explicit track color.</summary>
            [ComposeParameter]
            public SRgba<float> TrackColor { get; set => field = ControlStyle.Validate(value); } = new(0.2f, 0.3f, 0.5f, 1);

            /// <summary>Gets or sets the explicit thumb color.</summary>
            [ComposeParameter]
            public SRgba<float> ThumbColor { get; set => field = ControlStyle.Validate(value); } = new(1, 1, 1, 1);

            /// <summary>Gets or sets the declared finite value, silently clamped on adoption.</summary>
            [ComposeParameter]
            public SliderRange Value { get; set; } = new(0, 1);

            /// <summary>Gets or sets the latest changed callback, after atomic acceptance.</summary>
            [ComposeParameter]
            public Action<SliderRange>? OnChanged { get; set; }

            /// <summary>Gets or sets the completed-gesture callback, including unchanged gestures.</summary>
            [ComposeParameter]
            public Action<SliderRange>? OnCommitted { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new RangeSliderElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                SliderScale.Validate(Min, Max, Step);
                if (!double.IsFinite(Value.Lower) || !double.IsFinite(Value.Upper) || Value.Lower > Value.Upper || !Enum.IsDefined(Orientation))
                {
                    throw new ArgumentOutOfRangeException(nameof(Value));
                }
            }
        }
    }
}
