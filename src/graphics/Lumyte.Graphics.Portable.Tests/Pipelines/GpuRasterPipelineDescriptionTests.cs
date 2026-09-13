namespace Lumyte.Graphics.Portable.Tests.Pipelines;

public sealed class GpuRasterPipelineDescriptionTests
{
    [Fact]
    public void PipelineOwnsItsTargetOrderAfterTheSourceArrayChanges()
    {
        var first = new GpuColorTargetDescription(GpuFormat.Rgba8Unorm, GpuColorWriteMask.Red);
        var second = new GpuColorTargetDescription(GpuFormat.Bgra8Unorm, GpuColorWriteMask.Alpha,
            new GpuBlendDescription(SourceColorFactor: GpuBlendFactor.SourceAlpha));
        GpuColorTargetDescription[] targets = [first, second];

        var description = new GpuRasterPipelineDescription(targets);
        Array.Reverse(targets);
        targets[0] = default;

        Assert.Collection(description.ColorTargets,
            target => Assert.Equal(first, target), target => Assert.Equal(second, target));
    }

    [Fact]
    public void PipelineTargetsCannotBeMutatedThroughCollectionInterfaces()
    {
        var description = new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]);

        Assert.Throws<NotSupportedException>(() => ((IList<GpuColorTargetDescription>)description.ColorTargets).Clear());
    }

    [Fact]
    public void TargetWithoutBlendLeavesBlendingDisabled()
    {
        var target = new GpuColorTargetDescription(GpuFormat.Rgba8Unorm);

        Assert.Null(target.Blend);
    }

    [Fact]
    public void DepthOnlyPipelineDoesNotRequireColorTargets()
    {
        var description = new GpuRasterPipelineDescription([], GpuFormat.D32Float)
        {
            DepthStencil = new(DepthTest: true, DepthWrite: true),
        };

        Assert.Empty(description.ColorTargets);
        Assert.Equal(GpuFormat.D32Float, description.DepthStencilFormat);
    }
}
