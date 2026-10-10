using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSurface : IGraphicsSurface
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private BrowserSwapchain? _swapchain;
    private bool _disposed;

    internal BrowserSurface(BrowserDevice owner, JSObject context)
    {
        _owner = owner;
        _native = BrowserInterop.CreateSurface(owner.Handle, context);
        owner.RetainSurface();
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
        if (_swapchain != null)
        {
            throw new InvalidOperationException("The surface already owns a swapchain.");
        }

        _swapchain = new(this, desc);
        return _swapchain;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_swapchain != null)
        {
            throw new InvalidOperationException("Dispose the swapchain before its surface.");
        }

        BrowserInterop.DestroySurface(_native);
        _native.Dispose();
        _disposed = true;
        _owner.ReleaseSurface();
    }

    internal void Configure(SwapchainDesc desc)
    {
        SurfaceValidation.Validate(desc, GetCapabilities());
        BrowserInterop.ConfigureSurface(_native, checked((int)desc.Width), checked((int)desc.Height), (int)desc.Format, (int)desc.Usage, (int)desc.AlphaMode);
    }

    internal void ReleaseSwapchain() => _swapchain = null;

    internal void ValidateAlive()
    {
        _owner.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
