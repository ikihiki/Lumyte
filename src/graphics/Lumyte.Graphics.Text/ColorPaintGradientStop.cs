namespace Lumyte.Graphics.Text;

/// <summary>One offset and color pair in a COLRv1 gradient.</summary>
internal readonly record struct ColorPaintGradientStop(float Offset, ColorPaintColor Color);
