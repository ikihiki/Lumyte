namespace Lumyte.Graphics.Native;

/// <summary>A non-owning texture interpretation and subresource range. Constructing it creates no native object.</summary>
public readonly record struct NativeGpuTextureView(
    NativeGpuTextureHandle Texture,
    NativeGpuTextureViewDimension Dimension,
    GpuFormat Format,
    NativeGpuTextureAspect Aspect,
    uint BaseMip,
    uint MipCount,
    uint BaseLayer,
    uint LayerCount);
