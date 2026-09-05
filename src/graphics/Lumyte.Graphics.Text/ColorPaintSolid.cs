namespace Lumyte.Graphics.Text;

/// <summary>Fills the active COLRv1 clip with a solid color.</summary>
internal sealed record ColorPaintSolid(ColorPaintColor Color) : ColorPaintOperation;
