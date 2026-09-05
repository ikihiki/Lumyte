using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>A resolved COLRv1 color and whether its RGB channels represent the foreground color.</summary>
internal readonly record struct ColorPaintColor(Color Color, bool IsForeground);
