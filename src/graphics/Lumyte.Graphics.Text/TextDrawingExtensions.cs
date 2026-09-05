using System.Numerics;

using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Adds HarfBuzz-shaped text to a 2D command encoder.</summary>
public static class TextDrawingExtensions
{
    public static TextDrawResult DrawText(
        this CommandEncoder encoder,
        TextRenderer renderer,
        FontFace font,
        string text,
        Vector2 baseline,
        float fontSize,
        Brush brush,
        TextDrawOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return renderer.DrawText(encoder, font, text, baseline, fontSize, brush, options);
    }

    public static TextDrawResult DrawText(
        this CommandEncoder encoder,
        TextRenderer renderer,
        ShapedText text,
        Vector2 baseline,
        float fontSize,
        Brush brush,
        TextDrawOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return renderer.DrawText(encoder, text, baseline, fontSize, brush, options);
    }
}
