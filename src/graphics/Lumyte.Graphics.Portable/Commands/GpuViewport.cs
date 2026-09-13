namespace Lumyte.Graphics.Portable;

/// <summary>The floating-point viewport and depth range used by a render pass.</summary>
public readonly record struct GpuViewport(float X, float Y, float Width, float Height, float MinDepth = 0, float MaxDepth = 1);
