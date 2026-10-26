namespace Lumyte.Resources.Graphics.AotSmoke;

internal sealed class SmokeShader(ShaderArtifact artifact) : IGraphicsShader
{
    public ShaderArtifact Artifact => artifact;

    internal bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}
