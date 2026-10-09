using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSampler : IGraphicsSampler
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private bool _disposed;

    internal BrowserSampler(BrowserDevice owner, SamplerDesc desc)
    {
        (_owner, Desc) = (owner, desc);
        _native = BrowserInterop.CreateSampler(owner.Handle, System.Text.Json.JsonSerializer.Serialize(desc, BrowserJsonContext.Default.SamplerDesc));
    }

    public SamplerDesc Desc { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _native.Dispose();
        _disposed = true;
        _owner.ReleaseSampler();
    }
}
