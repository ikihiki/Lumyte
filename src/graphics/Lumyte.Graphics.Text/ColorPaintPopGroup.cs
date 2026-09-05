namespace Lumyte.Graphics.Text;

/// <summary>Pops and composites the active COLRv1 group.</summary>
internal sealed record ColorPaintPopGroup(ColorPaintCompositeMode CompositeMode) : ColorPaintOperation;
