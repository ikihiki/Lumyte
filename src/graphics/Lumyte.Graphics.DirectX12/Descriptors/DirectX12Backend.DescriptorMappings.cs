using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    private const uint NativeDefaultShaderComponentMapping = 5768;

    internal static ShaderResourceViewDesc ReadBufferDescription(ulong offset, ulong size)
    {
        uint elements = RawBufferElements(offset, size);
        return new ShaderResourceViewDesc
        {
            Format = Format.FormatR32Typeless, ViewDimension = SrvDimension.Buffer,
            Shader4ComponentMapping = NativeDefaultShaderComponentMapping,
            Buffer = new BufferSrv(offset / 4, elements, 0, BufferSrvFlags.Raw),
        };
    }

    internal static UnorderedAccessViewDesc WriteBufferDescription(ulong offset, ulong size)
    {
        uint elements = RawBufferElements(offset, size);
        return new UnorderedAccessViewDesc
        {
            Format = Format.FormatR32Typeless, ViewDimension = UavDimension.Buffer,
            Buffer = new BufferUav(offset / 4, elements, 0, 0, BufferUavFlags.Raw),
        };
    }

    private static uint RawBufferElements(ulong offset, ulong size)
    {
        // FirstElement/NumElements cannot carry byte remainders to native validation.
        if (offset % 4 != 0) { throw new ArgumentException("A raw descriptor offset must be representable in 32-bit elements.", nameof(offset)); }
        if (size % 4 != 0) { throw new ArgumentException("A raw descriptor size must be representable in 32-bit elements.", nameof(size)); }
        return checked((uint)(size / 4));
    }

    internal static SamplerDesc SamplerDescription(NativeGpuSamplerDescription description)
    {
        uint min = FilterBit(description.MinFilter);
        uint mag = FilterBit(description.MagFilter);
        uint mip = FilterBit(description.MipFilter);
        uint anisotropy = checked((uint)description.MaxAnisotropy);
        if (anisotropy != description.MaxAnisotropy)
        {
            throw new ArgumentException("Direct3D 12 represents anisotropy as an integer.", nameof(description));
        }
        uint bits = min << 4 | mag << 2 | mip | (description.CompareEnabled ? 0x80u : 0);
        if (anisotropy != 1)
        {
            if (min != 1 || mag != 1 || mip != 1)
            {
                throw new ArgumentException("Direct3D 12 anisotropic filtering requires linear min, mag and mip filters.", nameof(description));
            }
            bits |= 0x40;
        }
        return new SamplerDesc
        {
            Filter = (Filter)bits,
            AddressU = SamplerAddress(description.AddressU),
            AddressV = SamplerAddress(description.AddressV),
            AddressW = SamplerAddress(description.AddressW),
            MinLOD = description.MinLod, MaxLOD = description.MaxLod,
            MaxAnisotropy = anisotropy, ComparisonFunc = SamplerComparison(description.CompareOp),
        };
    }

    private static uint FilterBit(NativeGpuSamplerFilter filter) => filter switch
    {
        NativeGpuSamplerFilter.Nearest => 0,
        NativeGpuSamplerFilter.Linear => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static TextureAddressMode SamplerAddress(NativeGpuSamplerAddressMode mode) => mode switch
    {
        NativeGpuSamplerAddressMode.Repeat => TextureAddressMode.Wrap,
        NativeGpuSamplerAddressMode.MirrorRepeat => TextureAddressMode.Mirror,
        NativeGpuSamplerAddressMode.ClampToEdge => TextureAddressMode.Clamp,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static ComparisonFunc SamplerComparison(GpuCompareOp compare) => compare switch
    {
        GpuCompareOp.Never => ComparisonFunc.Never,
        GpuCompareOp.Less => ComparisonFunc.Less,
        GpuCompareOp.Equal => ComparisonFunc.Equal,
        GpuCompareOp.LessEqual => ComparisonFunc.LessEqual,
        GpuCompareOp.Greater => ComparisonFunc.Greater,
        GpuCompareOp.NotEqual => ComparisonFunc.NotEqual,
        GpuCompareOp.GreaterEqual => ComparisonFunc.GreaterEqual,
        GpuCompareOp.Always => ComparisonFunc.Always,
        _ => throw new ArgumentOutOfRangeException(nameof(compare)),
    };
}
