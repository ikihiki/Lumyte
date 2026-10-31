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
            /// <summary>Gets or sets a value indicating whether this node and its descendants are clipped to its local bounds.</summary>
            [ComposeParameter]
            public bool ClipToBounds { get; set; }

            internal void InvokeRecord(DrawingCanvas2D canvas) => RecordOverride(canvas);

            /// <summary>Records this component and its children in component-defined order.</summary>
            /// <param name="canvas">The active canvas with this node's origin and clip applied.</param>
            protected virtual void RecordOverride(DrawingCanvas2D canvas)
            {
                RecordSelf(canvas);
                foreach (WidgetChildren collection in ChildCollections)
                {
                    foreach (Widget child in collection)
                    {
                        RecordChild(child, canvas);
                    }
                }
            }

            /// <summary>Records OnRecord with canvas changes isolated from subsequent children.</summary>
            /// <param name="canvas">The active borrowed canvas.</param>
            protected void RecordSelf(DrawingCanvas2D canvas)
            {
                canvas.Save();
                try
                {
                    OnRecord(canvas);
                }
                finally
                {
                    canvas.Restore();
                }
            }

            /// <summary>Records one direct child, preserving its local origin, clip and caller canvas state.</summary>
            /// <param name="child">A child from any collection owned by this widget.</param>
            /// <param name="canvas">The active borrowed canvas.</param>
            protected void RecordChild(Widget child, DrawingCanvas2D canvas)
            {
                ArgumentNullException.ThrowIfNull(child);
                if (!ReferenceEquals(child.Owner, this))
                {
                    throw new ArgumentException("Recording requires a direct child of this widget.", nameof(child));
                }

                UiSurface.RecordWidget(child, canvas);
            }

            /// <summary>Records local drawing without changing the tree or controlling the borrowed canvas lifetime.</summary>
            /// <param name="canvas">The active canvas with this node's origin and clip applied; saves must be balanced.</param>
            protected virtual void OnRecord(DrawingCanvas2D canvas)
            {
            }
        }
    }
}
