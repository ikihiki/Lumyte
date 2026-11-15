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
        if (signalSemaphore != null && (signalSemaphore is not BrowserSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner)))
        {
            throw new ArgumentException("Acquisition semaphore belongs to another device.", nameof(signalSemaphore));
        }

        var status = (SurfaceStatus)BrowserInterop.GetSurfaceStatus(_surface.Native);
        if (status != SurfaceStatus.Success)
        {
            return ValueTask.FromResult(new SurfaceAcquireResult(status, null));
        }

        var frame = new BrowserSurfaceFrame(this, BrowserInterop.AcquireSurfaceTexture(_surface.Native));

        // Acquisition is synchronous, including when no semaphore was supplied;
        // completion retires readiness without consuming the binary signal.
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
