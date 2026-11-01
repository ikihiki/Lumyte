using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    internal void InvokeRecord(DrawingCanvas2D canvas) => RecordOverride(canvas);

    /// <summary>Records this component and its children in component-defined order.</summary>
    /// <param name="canvas">The active canvas with this node's origin and clip applied.</param>
    protected virtual void RecordOverride(DrawingCanvas2D canvas)
    {
        RecordSelf(canvas);
        foreach (UiElementChildren collection in Slots)
        {
            foreach (UiElement child in collection)
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
    protected void RecordChild(UiElement child, DrawingCanvas2D canvas)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!ReferenceEquals(child.Parent, this))
        {
            throw new ArgumentException("Recording requires a direct child of this widget.", nameof(child));
        }

        UiSurface.RecordElement(child, canvas);
    }

    /// <summary>Records local drawing without changing the tree or controlling the borrowed canvas lifetime.</summary>
    /// <param name="canvas">The active canvas with this node's origin and clip applied; saves must be balanced.</param>
    protected virtual void OnRecord(DrawingCanvas2D canvas)
    {
    }
}
