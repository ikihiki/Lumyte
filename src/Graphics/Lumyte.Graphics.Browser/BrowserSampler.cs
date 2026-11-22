using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSampler : IGraphicsSampler, IGraphicsNativeOrigin
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private bool _disposed;

    internal BrowserSampler(BrowserDevice owner, SamplerDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        _native = BrowserInterop.CreateSampler(owner.Handle, System.Text.Json.JsonSerializer.Serialize(desc, BrowserJsonContext.Default.SamplerDesc));
    }

    IGraphicDevice IGraphicsNativeOrigin.Device => Owner;

    object IGraphicsNativeOrigin.AllocationIdentity => this;

    public SamplerDesc Desc { get; }

    internal System.Runtime.InteropServices.JavaScript.JSObject Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _native;
        }
    }

    internal BrowserDevice Owner => _owner;

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
