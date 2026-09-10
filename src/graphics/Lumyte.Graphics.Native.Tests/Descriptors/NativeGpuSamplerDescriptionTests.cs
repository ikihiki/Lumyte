namespace Lumyte.Graphics.Native.Tests.Descriptors;

public sealed class NativeGpuSamplerDescriptionTests
{
    [Fact]
    public void ParameterlessConstructionCreatesAnUnboundedLinearRepeatingSampler()
    {
        var description = new NativeGpuSamplerDescription();

        Assert.Equal(
            (NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Linear,
                NativeGpuSamplerAddressMode.Repeat, NativeGpuSamplerAddressMode.Repeat, NativeGpuSamplerAddressMode.Repeat,
                0f, float.MaxValue, 1f, false, GpuCompareOp.Always),
            (description.MinFilter, description.MagFilter, description.MipFilter,
                description.AddressU, description.AddressV, description.AddressW,
                description.MinLod, description.MaxLod, description.MaxAnisotropy, description.CompareEnabled, description.CompareOp));
    }
}
