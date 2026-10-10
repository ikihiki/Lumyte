using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSwapchain : IGraphicsSwapchain
{
    private readonly BrowserSurface _surface;
    private BrowserSurfaceFrame? _active;
    private int _frameCount;
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
        if (_frameCount != 0)
        {
            throw new InvalidOperationException("Release all frames before reconfiguration.");
        }

        _surface.Configure(desc);
        Configuration = desc;
    }

    public ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        cancellationToken.ThrowIfCancellationRequested();
        if (_active != null)
        {
            throw new InvalidOperationException("Submit and present or discard the active canvas image before acquisition.");
        }

        var status = (SurfaceStatus)BrowserInterop.GetSurfaceStatus(_surface.Native);
        if (status != SurfaceStatus.Success)
        {
            return ValueTask.FromResult(new SurfaceAcquireResult(status, null));
        }

        _active = new(this, BrowserInterop.AcquireSurfaceTexture(_surface.Native));
        _frameCount++;
        return ValueTask.FromResult(new SurfaceAcquireResult(SurfaceStatus.Success, _active));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_frameCount != 0)
        {
            throw new InvalidOperationException("Release all frames before swapchain disposal.");
        }

        BrowserInterop.UnconfigureSurface(_surface.Native);
        _disposed = true;
        _surface.ReleaseSwapchain();
    }

    internal SurfaceStatus Present()
    {
        // Browser composition is implicit; this call closes the logical frame, not a GPU submission.
        var status = (SurfaceStatus)BrowserInterop.GetSurfaceStatus(_surface.Native);
        _active = null;
        return status;
    }

    internal void ReleaseFrame(BrowserSurfaceFrame frame)
    {
        if (ReferenceEquals(_active, frame))
        {
            _active = null;
        }

        _frameCount--;
    }

    private void ValidateAlive()
    {
        _surface.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
