using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuShader(CpuGraphicsDevice owner, ShaderArtifact artifact) : CpuResource(owner), IGraphicsShader
{
    public ShaderArtifact Artifact => artifact;
}
