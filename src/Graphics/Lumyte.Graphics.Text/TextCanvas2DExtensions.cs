using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>Connects immutable text layout snapshots to the ordinary 2D command recorder.</summary>
public static class TextCanvas2DExtensions
{
    /// <summary>Records text at a local offset, preserving the canvas state and ordered glyph paints.</summary>
    /// <param name="canvas">An actively recording canvas.</param>
    /// <param name="layout">The reusable text snapshot.</param>
    /// <param name="origin">An additional finite offset from the layout origin.</param>
    public static void DrawText(this DrawingCanvas2D canvas, TextLayout2D layout, Vector2 origin = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Drawings.Any(drawing => drawing is not TextGeometry2D and not TextPaintGeometry2D))
        {
            throw new NotSupportedException("Text drawings must contain vector outlines or paint graphs.");
        }

        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        canvas.Save();
        try
        {
            canvas.Translate(origin);
            foreach (TextDrawing2D drawing in layout.Drawings)
            {
                switch (drawing)
                {
                    case TextGeometry2D geometry:
                        canvas.FillPath(geometry.Path, geometry.Paint);
                        break;
                    case TextPaintGeometry2D geometry:
                        canvas.FillRect(geometry.Bounds, geometry.Paint);
                        break;
                    default:
                        throw new NotSupportedException("This text drawing is not a vector outline or paint graph.");
                }
            }
        }
        finally
        {
            canvas.Restore();
        }
    }
}
