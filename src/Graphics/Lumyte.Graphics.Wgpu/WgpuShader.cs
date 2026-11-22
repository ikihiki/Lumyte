using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuShader(WgpuDevice owner, ShaderArtifact artifact) : IGraphicsShader, IGraphicsNativeOrigin
{
    private readonly A.ShaderModule _native = owner.NativeDevice.CreateShaderModule(new A.ShaderModuleDescriptor
    {
        Source = A.ShaderSource.FromWgsl(artifact.GetTarget(owner.Caps.ShaderTarget).Code),
    });

    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

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

    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        ObjectDisposedException.ThrowIf(Owner.Status == GraphicsDeviceStatus.Disposed, Owner);
        if (Owner.Status == GraphicsDeviceStatus.Lost)
        {
            throw new GraphicsDeviceLostException(Owner.Loss!);
        }

        _ = Native;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _native.Dispose();
        _disposed = true;
    }
}
