using System.Runtime.InteropServices.JavaScript;
using System.Text;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserShader(BrowserDevice owner, ShaderArtifact artifact) : IGraphicsShader, IGraphicsNativeOrigin
{
    private readonly JSObject _handle = BrowserInterop.CreateShader(owner.Handle, Encoding.UTF8.GetString(artifact.GetTarget(owner.Caps.ShaderTarget).Code));
    private bool _disposed;

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

    public ShaderArtifact Artifact { get; } = artifact;

    internal BrowserDevice Owner => owner;

    internal JSObject Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
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

        _handle.Dispose();
        _disposed = true;
    }
}
