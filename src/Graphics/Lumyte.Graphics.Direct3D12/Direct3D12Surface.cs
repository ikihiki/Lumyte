using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12Surface : IGraphicsSurface
{
    private readonly Direct3D12Device _owner;
    private readonly nint _window;
    private bool _disposed;

    internal Direct3D12Surface(Direct3D12Device owner, nint window)
    {
        (_owner, _window) = (owner, window);
        _owner.Validation?.Register(this, _owner);
    }

    internal Direct3D12Device Owner => _owner;

    internal nint Window => _window;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Owner.Validation?.Release(this);
        _disposed = true;
    }

    internal void ValidateAlive()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.RequireReady();
        if (NativeMethods.WindowValid(_window) == 0)
        {
            throw new InvalidOperationException("The borrowed HWND is no longer valid.");
        }
    }
}
