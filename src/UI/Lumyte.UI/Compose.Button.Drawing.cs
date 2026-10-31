using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container with one content child and an ordered background slot.</summary>
        public partial class Button
        {
            /// <inheritdoc/>
            protected override void RecordOverride(DrawingCanvas2D canvas)
            {
                foreach (Widget child in BackgroundChildren)
                {
                    RecordChild(child, canvas);
                }

                RecordSelf(canvas);
                foreach (Widget child in Children)
                {
                    RecordChild(child, canvas);
                }
            }
        }
    }
}
