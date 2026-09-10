namespace Lumyte.Graphics.Native;

/// <summary>Native texture creation values. Resource compatibility is checked by the native API.</summary>
public readonly record struct NativeGpuTextureDescription(
    NativeGpuTextureDimension Dimension,
    uint Width,
    uint Height,
    uint Depth,
    uint MipCount,
    uint LayerCount,
    uint SampleCount,
    GpuFormat Format,
    NativeGpuTextureUsage Usage,
    bool MutableFormat = false);
