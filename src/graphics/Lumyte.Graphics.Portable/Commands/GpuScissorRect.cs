namespace Lumyte.Graphics.Portable;

/// <summary>The integer pixel rectangle in which raster output is allowed.</summary>
public readonly record struct GpuScissorRect(uint X, uint Y, uint Width, uint Height);
