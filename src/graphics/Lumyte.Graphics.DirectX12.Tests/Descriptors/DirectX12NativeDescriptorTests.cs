using Lumyte.Graphics.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12NativeDescriptorTests
{
    [Fact]
    public void SlotArithmeticUsesTheNativeHandleIncrement()
    {
        CpuDescriptorHandle handle = DirectX12Backend.DescriptorSlot(new(1000), 37, 8, 7);

        Assert.Equal((nuint)1259, handle.Ptr);
    }

    [Fact]
    public void SlotArithmeticRejectsTheEndOfTheHeap()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            DirectX12Backend.DescriptorSlot(new(1000), 37, 8, 8));

        Assert.Equal("index", error.ParamName);
    }

    [Fact]
    public void SlotArithmeticDoesNotWrapTheNativeHandle()
    {
        Assert.Throws<OverflowException>(() => DirectX12Backend.DescriptorSlot(new(nuint.MaxValue), 32, 2, 1));
    }

    [Fact]
    public void RawBufferDescriptionsPreserveRegionRelativeBytes()
    {
        ShaderResourceViewDesc read = DirectX12Backend.ReadBufferDescription(516, 1028);
        UnorderedAccessViewDesc write = DirectX12Backend.WriteBufferDescription(516, 1028);

        Assert.Equal((Format.FormatR32Typeless, SrvDimension.Buffer, 129ul, 257u, 0u, BufferSrvFlags.Raw),
            (read.Format, read.ViewDimension, read.Buffer.FirstElement, read.Buffer.NumElements,
                read.Buffer.StructureByteStride, read.Buffer.Flags));
        Assert.Equal((Format.FormatR32Typeless, UavDimension.Buffer, 129ul, 257u, 0u, 0ul, BufferUavFlags.Raw),
            (write.Format, write.ViewDimension, write.Buffer.FirstElement, write.Buffer.NumElements,
                write.Buffer.StructureByteStride, write.Buffer.CounterOffsetInBytes, write.Buffer.Flags));
    }

    [Theory]
    [InlineData(1ul, 4ul, "offset")]
    [InlineData(4ul, 5ul, "size")]
    public void RawBufferDescriptionsDoNotTruncateByteRemainders(ulong offset, ulong size, string parameter)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => DirectX12Backend.ReadBufferDescription(offset, size));

        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void RawBufferElementCountDoesNotTruncate()
    {
        Assert.Throws<OverflowException>(() => DirectX12Backend.WriteBufferDescription(0, ((ulong)uint.MaxValue + 1) * 4));
    }

    [Theory]
    [InlineData(NativeGpuSamplerFilter.Nearest, NativeGpuSamplerFilter.Nearest, NativeGpuSamplerFilter.Nearest, false, 0)]
    [InlineData(NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Nearest, NativeGpuSamplerFilter.Linear, false, 0x11)]
    [InlineData(NativeGpuSamplerFilter.Nearest, NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Nearest, true, 0x84)]
    [InlineData(NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Linear, true, 0x95)]
    public void SamplerFiltersKeepIndependentComponents(NativeGpuSamplerFilter min, NativeGpuSamplerFilter mag,
        NativeGpuSamplerFilter mip, bool comparison, int expected)
    {
        SamplerDesc native = DirectX12Backend.SamplerDescription(new(MinFilter: min, MagFilter: mag,
            MipFilter: mip, CompareEnabled: comparison));

        Assert.Equal((Filter)expected, native.Filter);
    }

    [Fact]
    public void SamplerNumericValuesAndAddressesReachNativeDescription()
    {
        var description = new NativeGpuSamplerDescription(AddressU: NativeGpuSamplerAddressMode.MirrorRepeat,
            AddressV: NativeGpuSamplerAddressMode.ClampToEdge, AddressW: NativeGpuSamplerAddressMode.Repeat,
            MinLod: 1.25f, MaxLod: 7.5f, MaxAnisotropy: 8, CompareEnabled: true, CompareOp: GpuCompareOp.GreaterEqual);

        SamplerDesc native = DirectX12Backend.SamplerDescription(description);

        Assert.Equal((Filter.ComparisonAnisotropic, TextureAddressMode.Mirror, TextureAddressMode.Clamp,
                TextureAddressMode.Wrap, 1.25f, 7.5f, 8u, ComparisonFunc.GreaterEqual),
            (native.Filter, native.AddressU, native.AddressV, native.AddressW, native.MinLOD,
                native.MaxLOD, native.MaxAnisotropy, native.ComparisonFunc));
    }

    [Fact]
    public void FractionalAnisotropyIsNotRounded()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            DirectX12Backend.SamplerDescription(new(MaxAnisotropy: 2.5f)));

        Assert.Equal("description", error.ParamName);
    }

    [Fact]
    public void AnisotropyDoesNotSilentlyOverridePointFiltering()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            DirectX12Backend.SamplerDescription(new(MipFilter: NativeGpuSamplerFilter.Nearest, MaxAnisotropy: 8)));

        Assert.Equal("description", error.ParamName);
    }
}
