using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Pushes a glyph-outline clip onto the COLRv1 clip stack.</summary>
/// <remarks>A null path represents a valid empty glyph outline and clips all nested paints.</remarks>
internal sealed record ColorPaintPushClipGlyph(uint GlyphId, PathGeometry? Path) : ColorPaintOperation;
