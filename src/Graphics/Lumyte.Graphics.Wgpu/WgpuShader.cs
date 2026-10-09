using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuShader(WgpuDevice owner, ShaderArtifact artifact) : IGraphicsShader
{
    private readonly A.ShaderModule _native = owner.NativeDevice.CreateShaderModule(new A.ShaderModuleDescriptor
    {
        Source = A.ShaderSource.FromWgsl(artifact.GetTarget(owner.Caps.ShaderTarget).Code),
    });

    private bool _disposed;

    public ShaderArtifact Artifact { get; } = artifact;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _native.Dispose();
        _disposed = true;
        owner.ReleaseShader();
    }
}
