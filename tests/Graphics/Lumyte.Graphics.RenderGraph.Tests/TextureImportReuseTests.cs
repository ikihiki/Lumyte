using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.RenderGraph.Tests;

/// <summary>Verifies that image-producing adapters reuse the caller's imported allocation.</summary>
public sealed class TextureImportReuseTests
{
    /// <summary>An existing texture handle is reused without replacing its initial state or ownership.</summary>
    [Fact]
    public void ReusesExistingImportWithoutChangingInitialState()
    {
        using var graph = new RenderGraph();
        using var texture = new TestTexture();
        RenderGraphTexture original = graph.ImportTexture(texture, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead));
        RenderGraphTexture reused = graph.GetOrImportTexture(texture, TextureState.Undefined, default);
        Assert.Same(original, reused);
        graph.AddPass("sample", _ => { }).UseTexture(reused, TextureState.Sampled, new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead)).KeepAlive();
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Equal(1, plan.Statistics.Resources);
    }
}
