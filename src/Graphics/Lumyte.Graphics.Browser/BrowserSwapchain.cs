using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSwapchain : IGraphicsSwapchain
{
    private readonly BrowserSurface _surface;
    private bool _disposed;

    internal BrowserSwapchain(BrowserSurface surface, SwapchainDesc desc)
    {
        _surface = surface;
        surface.Configure(desc);
        Configuration = desc;
    }

    public SwapchainDesc Configuration { get; private set; }

    internal BrowserDevice Owner => _surface.Owner;

    public void Reconfigure(SwapchainDesc desc)
    {
        ValidateAlive();
        _surface.Configure(desc);
        Configuration = desc;
    }

    public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        cancellationToken.ThrowIfCancellationRequested();
        var status = (SurfaceStatus)BrowserInterop.GetSurfaceStatus(_surface.Native);
        if (status != SurfaceStatus.Success)
        {
            return ValueTask.FromResult(new SurfaceAcquireResult(status, null));
        }

        var frame = new BrowserSurfaceFrame(this, BrowserInterop.AcquireSurfaceTexture(_surface.Native));
        return ValueTask.FromResult(new SurfaceAcquireResult(SurfaceStatus.Success, frame));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BrowserInterop.UnconfigureSurface(_surface.Native);
        _disposed = true;
    }

    internal SurfaceStatus Present()
    {
        // Browser composition is implicit; this call closes the logical frame, not a GPU submission.
        var status = (SurfaceStatus)BrowserInterop.GetSurfaceStatus(_surface.Native);
        return status;
    }

    private void ValidateAlive()
    {
        _surface.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
