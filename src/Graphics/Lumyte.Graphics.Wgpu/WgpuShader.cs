using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuShader(WgpuDevice owner, ShaderArtifact artifact) : IGraphicsShader
{
    private readonly A.ShaderModule _native = owner.NativeDevice.CreateShaderModule(new A.ShaderModuleDescriptor
    {
        Source = A.ShaderSource.FromWgsl(artifact.GetTarget(owner.Caps.ShaderTarget).Code),
    });

    private int _pipelineCount;
    private bool _disposed;

    public ShaderArtifact Artifact { get; } = artifact;

    internal WgpuDevice Owner => owner;

    internal A.ShaderModule Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _native;
        }
    }

    public void Dispose()
    {
        if (_pipelineCount != 0)
        {
            throw new InvalidOperationException("Dispose all programs before their shader module.");
        }

        if (_disposed)
        {
            return;
        }

        _native.Dispose();
        _disposed = true;
        owner.ReleaseShader();
    }

    internal void RetainPipeline()
    {
        _ = Native;
        _pipelineCount++;
    }

    internal void ReleasePipeline() => _pipelineCount--;
}
