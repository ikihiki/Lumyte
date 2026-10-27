using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>An immutable filled glyph or decoration in layout coordinates.</summary>
/// <param name="Path">The closed outline, using the nonzero fill rule.</param>
/// <param name="Paint">The immutable paint in layout coordinates.</param>
public sealed record TextGeometry2D(Path2D Path, PaintGraph2D Paint) : TextDrawing2D;
