namespace Lumyte.Graphics.Native;

/// <summary>A non-owning texture interpretation and subresource range. Constructing it creates no native object.</summary>
/// <remarks>
/// A TwoD or TwoDArray attachment view of a ThreeD texture selects depth slices using BaseLayer/LayerCount.
/// A ThreeD view selects the whole mip volume and uses BaseLayer zero and LayerCount one.
/// Texture transitions and discards require that whole-volume representation for a ThreeD texture.
/// </remarks>
public readonly record struct NativeGpuTextureView(
    NativeGpuTextureHandle Texture,
    NativeGpuTextureViewDimension Dimension,
    GpuFormat Format,
    NativeGpuTextureAspect Aspect,
    uint BaseMip,
    uint MipCount,
    uint BaseLayer,
    uint LayerCount);
