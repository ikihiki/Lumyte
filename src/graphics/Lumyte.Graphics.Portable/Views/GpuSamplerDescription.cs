namespace Lumyte.Graphics.Portable;

/// <summary>Sampler values that are materialized by a binding. Anisotropy of one disables anisotropic filtering.</summary>
/// <remarks>
/// Use <c>new GpuSamplerDescription()</c> for usable defaults. The all-zero <c>default</c> struct retains
/// zero anisotropy and is not silently repaired. The runtime validates filtering, comparison, and LOD restrictions.
/// </remarks>
public readonly record struct GpuSamplerDescription(
    GpuSamplerFilter MinFilter = GpuSamplerFilter.Nearest,
    GpuSamplerFilter MagFilter = GpuSamplerFilter.Nearest,
    GpuSamplerFilter MipFilter = GpuSamplerFilter.Nearest,
    GpuSamplerAddressMode AddressU = GpuSamplerAddressMode.ClampToEdge,
    GpuSamplerAddressMode AddressV = GpuSamplerAddressMode.ClampToEdge,
    GpuSamplerAddressMode AddressW = GpuSamplerAddressMode.ClampToEdge,
    float MinLod = 0,
    float MaxLod = 32,
    uint MaxAnisotropy = 1,
    GpuCompareOp? Compare = null)
{
    /// <summary>Creates a nearest-filtered, clamp-to-edge sampler with LOD 0 through 32 and no comparison.</summary>
    public GpuSamplerDescription() : this(MinFilter: GpuSamplerFilter.Nearest) { }
}
