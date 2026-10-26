using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.Tests;

internal sealed class TestShader(ShaderArtifact artifact) : IGraphicsShader
{
    public ShaderArtifact Artifact => artifact;

    internal bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
