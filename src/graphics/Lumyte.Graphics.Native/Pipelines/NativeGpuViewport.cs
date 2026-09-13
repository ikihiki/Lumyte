namespace Lumyte.Graphics.Native;

/// <summary>A viewport in framebuffer coordinates, with its origin at the upper left.</summary>
public readonly record struct NativeGpuViewport(
    float X, float Y, float Width, float Height, float MinDepth = 0, float MaxDepth = 1);
