namespace Lumyte.Graphics.Portable;

/// <summary>Depth and stencil clear values. Use an explicit value when the corresponding load operation is Clear.</summary>
public readonly record struct GpuClearDepthStencil(float Depth, uint Stencil);
