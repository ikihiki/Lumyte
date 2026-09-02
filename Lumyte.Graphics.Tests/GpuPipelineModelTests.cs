using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuPipelineModelTests
{
    [Fact]
    public void DepthStencilOperationsRemainOutsideRasterPipelineContract()
    {
        var description = new GpuRasterPipelineDescription(
            [new(GpuFormat.Rgba8Unorm)],
            depthFormat: GpuFormat.D32Float)
        {
            Topology = GpuPrimitiveTopology.TriangleList,
            CullMode = GpuCullMode.Back,
        };

        GpuRasterPipelineDescription result = description.Validate();

        Assert.Same(description, result);
        Assert.Null(description.EmbeddedBlend);
        Assert.DoesNotContain(typeof(GpuRasterPipelineDescription).GetProperties(),
            property => property.PropertyType == typeof(GpuDepthStencilState));
    }

    [Fact]
    public void MultisampleStateRemainsContractOnlyUntilResolveCommandsExist()
    {
        var description = new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
        {
            SampleCount = 4,
            AlphaToCoverage = true,
        };

        description.Validate();

        Assert.Equal(4u, description.SampleCount);
        Assert.True(description.AlphaToCoverage);
        Assert.DoesNotContain(
            typeof(GpuCommandBuffer).GetMethods(),
            method => method.Name.Contains("Resolve", StringComparison.Ordinal));
    }

    [Fact]
    public void BlendDescriptionExpressesEquationWithoutFixedModes()
    {
        var description = new GpuBlendDescription(
            SourceColorFactor: GpuBlendFactor.SourceAlpha,
            DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha);

        GpuBlendDescription result = description.Validate();

        Assert.Equal(GpuBlendFactor.SourceAlpha, result.SourceColorFactor);
        Assert.Equal(GpuBlendFactor.OneMinusSourceAlpha, result.DestinationColorFactor);
    }

    [Fact]
    public void ValidatePreservesEveryConfigurableOption()
    {
        var blend = new GpuBlendDescription(
            GpuBlendOperation.ReverseSubtract,
            GpuBlendFactor.SourceAlpha,
            GpuBlendFactor.OneMinusDestinationColor,
            GpuBlendOperation.Maximum,
            GpuBlendFactor.DestinationAlpha,
            GpuBlendFactor.OneMinusSourceAlpha,
            GpuColorWriteMask.Red | GpuColorWriteMask.Alpha);
        var description = new GpuRasterPipelineDescription(
            [new(GpuFormat.Bgra8Unorm, GpuColorWriteMask.Red | GpuColorWriteMask.Green)],
            GpuFormat.Depth24PlusStencil8,
            GpuFormat.Depth24PlusStencil8)
        {
            Topology = GpuPrimitiveTopology.TriangleStrip,
            CullMode = GpuCullMode.Front,
            FrontFace = GpuFrontFace.Clockwise,
            SampleCount = 4,
            AlphaToCoverage = true,
            SupportsDualSourceBlending = true,
            EmbeddedBlend = blend,
        };

        GpuRasterPipelineDescription result = description.Validate();

        Assert.Same(description, result);
        Assert.Equal(
            new GpuColorTargetDescription(
                GpuFormat.Bgra8Unorm,
                GpuColorWriteMask.Red | GpuColorWriteMask.Green),
            Assert.Single(result.ColorTargets));
        Assert.Equal(GpuFormat.Depth24PlusStencil8, result.DepthFormat);
        Assert.Equal(GpuFormat.Depth24PlusStencil8, result.StencilFormat);
        Assert.Equal(GpuPrimitiveTopology.TriangleStrip, result.Topology);
        Assert.Equal(GpuCullMode.Front, result.CullMode);
        Assert.Equal(GpuFrontFace.Clockwise, result.FrontFace);
        Assert.Equal(4u, result.SampleCount);
        Assert.True(result.AlphaToCoverage);
        Assert.True(result.SupportsDualSourceBlending);
        Assert.Equal(blend, result.EmbeddedBlend);
    }

    [Theory]
    [InlineData("topology")]
    [InlineData("cull")]
    [InlineData("front-face")]
    public void ValidateRejectsUnknownRasterState(string option)
    {
        GpuRasterPipelineDescription description = option switch
        {
            "topology" => new([new(GpuFormat.Rgba8Unorm)])
            {
                Topology = (GpuPrimitiveTopology)int.MaxValue,
            },
            "cull" => new([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = (GpuCullMode)int.MaxValue,
            },
            "front-face" => new([new(GpuFormat.Rgba8Unorm)])
            {
                FrontFace = (GpuFrontFace)int.MaxValue,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => description.Validate());
    }
}
