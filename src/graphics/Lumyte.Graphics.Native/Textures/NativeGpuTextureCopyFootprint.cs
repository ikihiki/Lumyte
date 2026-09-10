namespace Lumyte.Graphics.Native;

/// <summary>A non-owning, single-aspect texture transfer with byte pitches relative to the range start.</summary>
/// <remarks>
/// RowPitch separates texel block rows. ImagePitch separates array layers or 3D depth slices.
/// The caller supplies native-compatible pitches and ranges; no staging or format conversion is performed.
/// </remarks>
public readonly record struct NativeGpuTextureCopyFootprint(
    uint Mip,
    NativeGpuTextureAspect Aspect,
    uint BaseLayer,
    uint LayerCount,
    GpuOrigin3D Origin,
    GpuExtent3D Extent,
    ulong RowPitch,
    ulong ImagePitch);
