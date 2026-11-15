using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class FactoryShader(ShaderArtifact artifact) : IGraphicsShader
{
    public ShaderArtifact Artifact => artifact;

    internal int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}
