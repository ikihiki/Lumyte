using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSurface : IGraphicsSurface
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private bool _disposed;

    internal BrowserSurface(BrowserDevice owner, JSObject context)
    {
        _owner = owner;
        _native = BrowserInterop.CreateSurface(owner.Handle, context);
    }

    internal BrowserDevice Owner => _owner;

    internal JSObject Native => _native;

    public SurfaceCapabilities GetCapabilities()
    {
        ValidateAlive();
        var preferred = (TextureFormat)BrowserInterop.GetSurfacePreferredFormat(_native);
        return new()
        {
            Formats = Array.AsReadOnly(new[] { preferred, preferred == TextureFormat.Bgra8Unorm ? TextureFormat.Rgba8Unorm : TextureFormat.Bgra8Unorm, TextureFormat.Rgba16Float }),
            PresentModes = Array.AsReadOnly(new[] { PresentMode.Fifo }),
            AlphaModes = Array.AsReadOnly(new[] { SurfaceAlphaMode.Auto, SurfaceAlphaMode.Opaque, SurfaceAlphaMode.Premultiplied }),
            SupportedUsage = TextureUsage.RenderAttachment | TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.Sampled,
            MaxWidth = _owner.Caps.MaxTextureDimension2D,
            MaxHeight = _owner.Caps.MaxTextureDimension2D,
        };
    }

    public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc)
    {
        ValidateAlive();
        return new BrowserSwapchain(this, desc);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BrowserInterop.DestroySurface(_native);
        _native.Dispose();
        _disposed = true;
    }

    internal void Configure(SwapchainDesc desc) => BrowserInterop.ConfigureSurface(_native, checked((int)desc.Width), checked((int)desc.Height), (int)desc.Format, (int)desc.Usage, (int)desc.AlphaMode);

    internal void ValidateAlive()
    {
        _owner.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
