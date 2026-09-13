namespace Lumyte.Graphics.Portable;

/// <summary>Resource creation values; no allocation, placement, or GPU address is exposed.</summary>
public readonly record struct GpuTextureDescription(
    GpuTextureDimension Dimension,
    uint Width,
    uint Height,
    uint Depth,
    uint MipCount,
    uint LayerCount,
    uint SampleCount,
    GpuFormat Format,
    GpuTextureUsage Usage,
    bool MutableFormat = false);
