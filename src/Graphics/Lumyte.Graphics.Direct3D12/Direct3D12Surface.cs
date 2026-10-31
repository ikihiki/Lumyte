using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Surface(Direct3D12Device owner, nint window) : IGraphicsSurface
{
    private bool _disposed;

    internal Direct3D12Device Owner => owner;

    internal nint Window => window;

    public SurfaceCapabilities GetCapabilities()
    {
        ValidateAlive();
        return new()
        {
            Formats = Array.AsReadOnly(new[] { TextureFormat.Bgra8Unorm, TextureFormat.Rgba8Unorm, TextureFormat.Rgba16Float, TextureFormat.Rgb10A2Unorm }),
            PresentModes = Array.AsReadOnly(NativeMethods.TearingSupported() != 0 ? new[] { PresentMode.Fifo, PresentMode.Immediate } : new[] { PresentMode.Fifo }),
            AlphaModes = Array.AsReadOnly(new[] { SurfaceAlphaMode.Auto, SurfaceAlphaMode.Opaque }),
            SupportedUsage = TextureUsage.RenderAttachment | TextureUsage.CopySource | TextureUsage.CopyDestination | TextureUsage.Sampled,
            MaxWidth = Owner.Caps.MaxTextureDimension2D,
            MaxHeight = Owner.Caps.MaxTextureDimension2D,
        };
    }

    public IGraphicsSwapchain CreateSwapchain(SwapchainDesc desc)
    {
        ValidateAlive();
        return new Direct3D12Swapchain(this, desc);
    }

    public void Dispose() => _disposed = true;

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.RequireReady();
        if (NativeMethods.WindowValid(window) == 0)
        {
            throw new InvalidOperationException("The borrowed HWND is no longer valid.");
        }
    }
}
