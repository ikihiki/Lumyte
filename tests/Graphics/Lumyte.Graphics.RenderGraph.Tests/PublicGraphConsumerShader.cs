using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerShader(ShaderArtifact artifact) : IGraphicsShader
{
    public ShaderArtifact Artifact => artifact;

    public void Dispose()
    {
    }
}
