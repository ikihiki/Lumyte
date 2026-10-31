using System.Numerics;
using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Positions children sequentially without shrinking them to the remaining space.</summary>
        [Composable]
        public partial class Stack : Panel
        {
            private float _spacing;

            /// <summary>Initializes a new instance of the <see cref="Stack"/> class.</summary>
            public Stack()
            {
            }

            /// <summary>Gets or sets the main axis, which defaults to vertical.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; }

            /// <summary>Gets or sets the finite nonnegative gap between visible children.</summary>
            [ComposeParameter]
            public float Spacing
            {
                get => _spacing;
                set
                {
                    UiLayoutMath.ValidateDimension(value, nameof(value));
                    _spacing = value;
                }
            }

            /// <inheritdoc/>
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                bool vertical = Orientation == UiOrientation.Vertical;
                UiBoxConstraints childConstraints = vertical
                    ? new(0, constraints.MaxWidth, 0, float.PositiveInfinity)
                    : new(0, float.PositiveInfinity, 0, constraints.MaxHeight);
                double mainSize = 0;
                double crossSize = 0;
                bool hasVisibleChild = false;
                foreach (Widget child in Children)
                {
                    Vector2 size = LayoutChild(child, childConstraints);
                    if (!child.IsVisible)
                    {
                        SetChildOffset(child, Vector2.Zero);
                        continue;
                    }

                    if (hasVisibleChild)
                    {
                        mainSize += _spacing;
                    }

                    float offset = UiLayoutMath.FiniteFloat(mainSize);
                    SetChildOffset(child, vertical ? new(0, offset) : new(offset, 0));
                    mainSize += vertical ? size.Y : size.X;
                    crossSize = Math.Max(crossSize, vertical ? size.X : size.Y);
                    hasVisibleChild = true;
                }

                return vertical
                    ? UiLayoutMath.FillBounded(constraints, crossSize, mainSize)
                    : UiLayoutMath.FillBounded(constraints, mainSize, crossSize);
            }
        }
    }
}
