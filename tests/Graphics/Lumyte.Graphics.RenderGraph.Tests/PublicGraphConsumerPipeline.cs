using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerPipeline(ShaderArtifact artifact) : IGraphicsPipeline
{
    public GraphicsPipelineDesc Desc { get; } = new() { VertexShader = new PublicGraphConsumerShader(artifact) };

    public void Dispose()
    {
    }
}
