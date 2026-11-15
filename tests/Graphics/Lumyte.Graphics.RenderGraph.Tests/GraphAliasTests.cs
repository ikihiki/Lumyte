using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Checks that future sampled attachments do not leak into earlier pass tables.</summary>
public sealed class GraphAliasTests
{
    /// <summary>All declarations stay available, but a later sampled output belongs to another table.</summary>
    [Fact]
    public void FutureSampledOutputDoesNotJoinEarlierPassTable()
    {
        var device = new PublicGraphConsumerDevice();
        using var graph = new RenderGraph(device);
        using var sourceImage = new TestTexture();
        using var intermediateImage = new TestTexture();
        RenderGraphTexture source = graph.ImportTexture(sourceImage, TextureState.Sampled, default);
        RenderGraphTexture intermediate = graph.ImportTexture(intermediateImage, TextureState.ColorAttachment, default);
        IShaderReference? first = null;
        IShaderReference? second = null;
        graph.AddPass("write intermediate", context => first = (IShaderReference)context.GetGpuRef(source))
            .UseTexture(source, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead))
            .UseTexture(intermediate, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        graph.AddPass("sample intermediate", context => second = (IShaderReference)context.GetGpuRef(intermediate))
            .UseTexture(intermediate, TextureState.Sampled, new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new PublicGraphConsumerCommands();

        plan.Record(commands);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first.Table, second.Table);
        Assert.Equal(2, device.Tables.Count);
        Assert.All(device.Tables, table => Assert.Equal(1u, table.TextureCapacity));
    }
}
