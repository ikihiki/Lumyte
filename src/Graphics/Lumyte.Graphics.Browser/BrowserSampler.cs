using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSampler : IGraphicsSampler
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private int _registrationCount;
    private bool _disposed;

    internal BrowserSampler(BrowserDevice owner, SamplerDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        _native = BrowserInterop.CreateSampler(owner.Handle, System.Text.Json.JsonSerializer.Serialize(desc, BrowserJsonContext.Default.SamplerDesc));
    }

    public SamplerDesc Desc { get; }

    internal BrowserDevice Owner => _owner;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registrationCount != 0)
        {
            throw new InvalidOperationException("Release all argument table registrations before disposing their resource.");
        }

        _native.Dispose();
        _disposed = true;
        _owner.ReleaseSampler();
    }

    internal void RetainRegistration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registrationCount = checked(_registrationCount + 1);
    }

    internal void ReleaseRegistration() => _registrationCount--;
}
