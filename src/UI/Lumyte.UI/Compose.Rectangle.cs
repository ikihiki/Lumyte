using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>Fills its final local bounds with a straight-alpha sRGB color.</summary>
        [Composable]
        public partial class Rectangle : Widget
        {
            private Color2D _fill;

            /// <summary>Initializes a new instance of the <see cref="Rectangle"/> class.</summary>
            public Rectangle()
            {
            }

            /// <summary>Gets or sets the fill; all components must be finite and between zero and one.</summary>
            [ComposeParameter]
            public Color2D Fill
            {
                get => _fill;
                set
                {
                    using MutationScope mutation = BeginMutation();
                    ValidateComponent(value.R);
                    ValidateComponent(value.G);
                    ValidateComponent(value.B);
                    ValidateComponent(value.A);
                    _fill = value;
                }
            }

            /// <inheritdoc/>
            protected override void OnRecord(DrawingCanvas2D canvas)
            {
                if (Size.X > 0 && Size.Y > 0 && _fill.A > 0)
                {
                    canvas.FillRect(new(0, 0, Size.X, Size.Y), _fill);
                }
            }

            private static void ValidateComponent(float value)
            {
                if (!float.IsFinite(value) || value < 0 || value > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Color components must be finite and between zero and one.");
                }
            }
        }
    }
}
