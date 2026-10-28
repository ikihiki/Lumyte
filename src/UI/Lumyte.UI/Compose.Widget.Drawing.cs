using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        public abstract partial class Widget
        {
            private bool _clipToBounds;

            /// <summary>Gets or sets a value indicating whether this node and its descendants are clipped to its local bounds.</summary>
            [ComposeParameter]
            public bool ClipToBounds
            {
                get => _clipToBounds;
                set
                {
                    using MutationScope mutation = BeginMutation();
                    _clipToBounds = value;
                }
            }

            internal void InvokeRecord(DrawingCanvas2D canvas) => OnRecord(canvas);

            /// <summary>Records local drawing without changing the tree or controlling the borrowed canvas lifetime.</summary>
            /// <param name="canvas">The active canvas with this node's origin and clip applied; saves must be balanced.</param>
            protected virtual void OnRecord(DrawingCanvas2D canvas)
            {
            }
        }
    }
}
