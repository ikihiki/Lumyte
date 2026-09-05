namespace Lumyte.Graphics.Text;

/// <summary>Pushes an isolated COLRv1 compositing group.</summary>
internal sealed record ColorPaintPushGroup(ColorPaintCompositeMode? CompositeMode) : ColorPaintOperation;
