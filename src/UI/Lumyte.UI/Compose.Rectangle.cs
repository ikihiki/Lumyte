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
            /// <summary>Initializes a new instance of the <see cref="Rectangle"/> class.</summary>
            public Rectangle()
            {
            }

            /// <summary>Gets or sets the fill, whose components are validated by Color2D.</summary>
            [ComposeParameter]
            public Color2D Fill { get; set; }

            /// <inheritdoc/>
            protected override bool SupportsTransitionCapture => GetType() == typeof(Rectangle);

            /// <inheritdoc/>
            protected override bool CanReuseTransitionCapture(Widget previous) => GetType() == typeof(Rectangle) && ((Rectangle)previous).Fill == Fill;

            /// <inheritdoc/>
            protected override bool CanReuseInput(Widget previous) => CanReuseLayout(previous);

            /// <inheritdoc/>
            protected override bool CanReuseLayout(Widget previous) => GetType() == typeof(Rectangle);

            /// <inheritdoc/>
            protected override void OnRecord(DrawingCanvas2D canvas)
            {
                if (Size.X > 0 && Size.Y > 0 && Fill.A > 0)
                {
                    canvas.FillRect(new(0, 0, Size.X, Size.Y), Fill);
                }
            }
        }
    }
}
