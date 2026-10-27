using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>A reusable immutable snapshot of shaped text outlines, independent of fonts and GPU resources.</summary>
public sealed class TextLayout2D
{
    private TextLayout2D(string text, Rect2D bounds, Rect2D advanceBounds, int lineCount, IEnumerable<TextDrawing2D> drawings)
    {
        Text = text;
        Bounds = bounds;
        AdvanceBounds = advanceBounds;
        LineCount = lineCount;
        Drawings = Array.AsReadOnly(drawings.ToArray());
        Geometry = Array.AsReadOnly(Drawings.OfType<TextGeometry2D>().ToArray());
    }

    /// <summary>Gets the original plain text.</summary>
    public string Text { get; }

    /// <summary>Gets conservative ink bounds including curve control points and decorations in layout coordinates.</summary>
    public Rect2D Bounds { get; }

    /// <summary>Gets the typographic advance bounds, including whitespace, from the layout engine.</summary>
    public Rect2D AdvanceBounds { get; }

    /// <summary>Gets the number of laid out lines.</summary>
    public int LineCount { get; }

    /// <summary>Gets the ordered immutable glyph and decoration outlines.</summary>
    public IReadOnlyList<TextGeometry2D> Geometry { get; }

    /// <summary>Gets all outlines and vector color paint graphs in paragraph painter order.</summary>
    public IReadOnlyList<TextDrawing2D> Drawings { get; }

    /// <summary>Copies a layout backend's completed vector output into an independent text snapshot.</summary>
    /// <param name="text">The original plain Unicode text.</param>
    /// <param name="bounds">Conservative finite ink bounds in layout coordinates.</param>
    /// <param name="advanceBounds">Finite typographic bounds, including whitespace.</param>
    /// <param name="lineCount">The nonnegative number of laid out lines.</param>
    /// <param name="drawings">Ordered vector outlines and paint graphs; copied before returning.</param>
    /// <returns>A snapshot independent of the layout backend and its font resources.</returns>
    public static TextLayout2D FromDrawings(string text, Rect2D bounds, Rect2D advanceBounds, int lineCount, IEnumerable<TextDrawing2D> drawings)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(drawings);
        ArgumentOutOfRangeException.ThrowIfNegative(lineCount);
        ValidateBounds(bounds, nameof(bounds));
        ValidateBounds(advanceBounds, nameof(advanceBounds));
        TextDrawing2D[] snapshot = drawings.ToArray();
        if (snapshot.Any(drawing => drawing is null))
        {
            throw new ArgumentException("Text drawings must not contain null entries.", nameof(drawings));
        }

        return new TextLayout2D(text, bounds, advanceBounds, lineCount, snapshot);
    }

    private static void ValidateBounds(Rect2D bounds, string parameterName)
    {
        if (!float.IsFinite(bounds.X) || !float.IsFinite(bounds.Y) || !float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width < 0 || bounds.Height < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Text bounds must be finite with nonnegative dimensions.");
        }
    }
}
