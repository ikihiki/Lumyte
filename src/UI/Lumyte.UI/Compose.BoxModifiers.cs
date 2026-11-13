using Lumyte.Composition;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains declarations consumed by Composition.</summary>
    public static partial class Definitions
    {
        /// <summary>Defines ordered inset operations, not a display widget.</summary>
        [Composable]
        public partial class Padding
        {
            [ComposeAction]
            private static void All(Widget target, float value) => Sides(target, value, value, value, value);

            [ComposeAction]
            private static void Sides(Widget target, float left, float top, float right, float bottom)
            {
                UiLayoutMath.ValidateDimension(left, nameof(left));
                UiLayoutMath.ValidateDimension(top, nameof(top));
                UiLayoutMath.ValidateDimension(right, nameof(right));
                UiLayoutMath.ValidateDimension(bottom, nameof(bottom));
                target.AddModifier(new(UiBoxModifierKind.Padding, left, top, right, bottom));
            }
        }

        /// <summary>Defines ordered inside-border operations, not a display widget.</summary>
        [Composable]
        public partial class Border
        {
            [ComposeAction]
            private static void Stroke(Widget target, SRgba<float> color, float width)
            {
                UiColorPolicy.Unit(color, nameof(color));
                UiLayoutMath.ValidateDimension(width, nameof(width));
                target.AddModifier(new(UiBoxModifierKind.Border, width, width, width, width, color));
            }
        }

        /// <summary>Defines ordered alignment operations, not a display widget.</summary>
        [Composable]
        public partial class Align
        {
            [ComposeAction]
            private static void Center(Widget target) => At(target, 0.5f, 0.5f);

            [ComposeAction]
            private static void At(Widget target, float x, float y)
            {
                if (!float.IsFinite(x) || x < 0 || x > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(x));
                }

                if (!float.IsFinite(y) || y < 0 || y > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(y));
                }

                target.AddModifier(new(UiBoxModifierKind.Align, x, y));
            }
        }

        /// <summary>Defines ordered additional constraints, not a display widget.</summary>
        [Composable]
        public partial class ConstrainedBox
        {
            [ComposeAction]
            private static void Limits(Widget target, UiBoxConstraints constraints) => target.AddModifier(new(UiBoxModifierKind.Constraints, Constraints: new(constraints.MinWidth, constraints.MaxWidth, constraints.MinHeight, constraints.MaxHeight)));
        }

        /// <summary>A passive, replaceable UI configuration.</summary>
        public abstract partial class Widget
        {
            private UiBoxModifier[] _boxModifiers = [];

            internal ReadOnlySpan<UiBoxModifier> BoxModifiers => _boxModifiers;

            internal void AddModifier(UiBoxModifier modifier) => _boxModifiers = [.. _boxModifiers, modifier];
        }
    }
}
