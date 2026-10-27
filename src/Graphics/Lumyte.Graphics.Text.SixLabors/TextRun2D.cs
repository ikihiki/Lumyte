using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;

namespace Lumyte.Graphics.Text;

/// <summary>A styled grapheme range with an optional 2D paint, font, attributes and decorations.</summary>
public sealed class TextRun2D : TextRun
{
    /// <summary>Gets or sets the run paint in layout coordinates; null uses the layout's default paint.</summary>
    public PaintGraph2D? Paint { get; set; }
}
