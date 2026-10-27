using System.Numerics;

namespace Lumyte.Graphics.Text.Parley;

/// <summary>A positioned glyph and its source cluster, independent of native owners.</summary>
/// <param name="GlyphId">The glyph identifier within its font face.</param>
/// <param name="FontId">The engine-local registered font identifier.</param>
/// <param name="FontFamily">The registered family that supplied the glyph.</param>
/// <param name="Utf8Start">The inclusive UTF-8 byte offset of the source cluster.</param>
/// <param name="Utf8Length">The cluster's UTF-8 byte length.</param>
/// <param name="Utf16Start">The inclusive UTF-16 index of the source cluster.</param>
/// <param name="Utf16Length">The cluster's UTF-16 length.</param>
/// <param name="Origin">The positioned alphabetic baseline origin in logical pixels.</param>
/// <param name="Advance">The horizontal advance in logical pixels.</param>
/// <param name="IsColor">Whether the glyph produced a color paint graph.</param>
public readonly record struct ParleyGlyph2D(uint GlyphId, uint FontId, string FontFamily, int Utf8Start, int Utf8Length, int Utf16Start, int Utf16Length, Vector2 Origin, float Advance, bool IsColor);
