using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    /// <summary>Updates layout and records all visible instances into a borrowed active canvas.</summary>
    /// <param name="canvas">The begun canvas, whose transform and clip are restored.</param>
    public void Record(DrawingCanvas2D canvas)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(canvas);

        // Save also validates Begin before layout can invoke user code.
        canvas.Save();
        try
        {
            UpdateLayout();
            if (!_isDisposed && _viewport.X > 0 && _viewport.Y > 0)
            {
                canvas.ClipRect(new(0, 0, _viewport.X, _viewport.Y));
                _recordDepth++;
                try
                {
                    RecordElement(_rootElement, canvas);
                }
                finally
                {
                    _recordDepth--;
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    internal static void RecordElement(UiElement element, DrawingCanvas2D canvas)
    {
        if (!element.Widget.IsVisible)
        {
            return;
        }

        canvas.Save();
        try
        {
            canvas.Translate(new(element.Bounds.X, element.Bounds.Y));
            if (element.Widget.ClipToBounds)
            {
                canvas.ClipRect(new(0, 0, element.Size.X, element.Size.Y));
            }

            element.InvokeRecord(canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }
}
