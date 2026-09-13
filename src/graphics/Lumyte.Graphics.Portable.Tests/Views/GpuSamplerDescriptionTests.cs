namespace Lumyte.Graphics.Portable.Tests.Views;

public sealed class GpuSamplerDescriptionTests
{
    [Fact]
    public void ConstructionProvidesUsableLodAndAnisotropyDefaults()
    {
        var sampler = new GpuSamplerDescription();

        Assert.Equal((0f, 32f, 1u, (GpuCompareOp?)null), (sampler.MinLod, sampler.MaxLod, sampler.MaxAnisotropy, sampler.Compare));
    }

    [Fact]
    public void NamedFilteringConstructionRetainsTheOtherDefaults()
    {
        var sampler = new GpuSamplerDescription(MinFilter: GpuSamplerFilter.Linear);

        Assert.Equal(new GpuSamplerDescription() with { MinFilter = GpuSamplerFilter.Linear }, sampler);
    }

    [Fact]
    public void ZeroInitializedSamplerIsNotSilentlyRepaired()
    {
        GpuSamplerDescription sampler = default;

        Assert.Equal(0u, sampler.MaxAnisotropy);
        Assert.NotEqual(new GpuSamplerDescription(), sampler);
    }
}
