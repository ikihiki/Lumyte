using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerComputePipeline(ShaderArtifact artifact) : IGraphicsComputePipeline
{
    public ComputePipelineDesc Desc { get; } = new() { ComputeShader = new PublicGraphConsumerShader(artifact) };

    public void Dispose()
    {
    }
}
