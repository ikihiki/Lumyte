using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal sealed class ProgramTestShader(ShaderArtifact artifact, Action release) : IGraphicsShader
{
    public ShaderArtifact Artifact { get; } = artifact;

    public void Dispose() => release();
}
