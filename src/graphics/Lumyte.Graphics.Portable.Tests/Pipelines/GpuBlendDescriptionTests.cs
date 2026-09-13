namespace Lumyte.Graphics.Portable.Tests.Pipelines;

public sealed class GpuBlendDescriptionTests
{
    [Fact]
    public void ParameterlessBlendUsesSourceReplacementForBothEquations()
    {
        var blend = new GpuBlendDescription();

        Assert.Equal(new GpuBlendDescription(GpuBlendOperation.Add, GpuBlendFactor.One, GpuBlendFactor.Zero,
            GpuBlendOperation.Add, GpuBlendFactor.One, GpuBlendFactor.Zero), blend);
    }

    [Fact]
    public void ZeroInitializedBlendDoesNotSilentlyBecomeSourceReplacement()
    {
        GpuBlendDescription blend = default;

        Assert.Equal(new GpuBlendDescription(SourceColorFactor: GpuBlendFactor.Zero,
            SourceAlphaFactor: GpuBlendFactor.Zero), blend);
    }
}
