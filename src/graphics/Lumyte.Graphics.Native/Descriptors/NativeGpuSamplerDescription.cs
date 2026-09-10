namespace Lumyte.Graphics.Native;

/// <summary>Native sampler values. Anisotropy of one disables anisotropic filtering.</summary>
/// <remarks>Native restrictions are not clamped or repaired by the backend.</remarks>
public readonly record struct NativeGpuSamplerDescription(
    NativeGpuSamplerFilter MinFilter = NativeGpuSamplerFilter.Linear,
    NativeGpuSamplerFilter MagFilter = NativeGpuSamplerFilter.Linear,
    NativeGpuSamplerFilter MipFilter = NativeGpuSamplerFilter.Linear,
    NativeGpuSamplerAddressMode AddressU = NativeGpuSamplerAddressMode.Repeat,
    NativeGpuSamplerAddressMode AddressV = NativeGpuSamplerAddressMode.Repeat,
    NativeGpuSamplerAddressMode AddressW = NativeGpuSamplerAddressMode.Repeat,
    float MinLod = 0,
    float MaxLod = float.MaxValue,
    float MaxAnisotropy = 1,
    bool CompareEnabled = false,
    GpuCompareOp CompareOp = GpuCompareOp.Always)
{
    /// <summary>Creates a linear, repeating sampler with anisotropic and comparison filtering disabled.</summary>
    public NativeGpuSamplerDescription() : this(MinFilter: NativeGpuSamplerFilter.Linear) { }
}
