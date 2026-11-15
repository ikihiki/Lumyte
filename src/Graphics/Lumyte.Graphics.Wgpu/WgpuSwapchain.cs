using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSwapchain : IGraphicsSwapchain
{
    private readonly WgpuSurface _surface;
    private bool _disposed;

    internal WgpuSwapchain(WgpuSurface surface, SwapchainDesc desc)
    {
        _surface = surface;
        surface.Configure(desc);
        Configuration = desc;
    }

    public SwapchainDesc Configuration { get; private set; }

    internal WgpuDevice Owner => _surface.Owner;

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
        if (signalSemaphore != null && (signalSemaphore is not WgpuSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner)))
        {
            throw new ArgumentException("Acquisition semaphore belongs to another device.", nameof(signalSemaphore));
        }

        Ahjo.Wgpu.SurfaceAcquireResult acquired = _surface.Native.GetCurrentTexture();
        SurfaceStatus status = acquired.Status switch
        {
            WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal => SurfaceStatus.Success,
            WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal => SurfaceStatus.Suboptimal,
            WGPUSurfaceGetCurrentTextureStatus.Timeout => SurfaceStatus.Timeout,
            WGPUSurfaceGetCurrentTextureStatus.Outdated => SurfaceStatus.Outdated,
            WGPUSurfaceGetCurrentTextureStatus.Lost => SurfaceStatus.Lost,
            _ => throw new InvalidOperationException("wgpu surface acquisition failed."),
        };
        if (!acquired.IsUsable)
        {
            acquired.Texture.Dispose();
            return ValueTask.FromResult(new SurfaceAcquireResult(status, null));
        }

        var frame = new WgpuSurfaceFrame(this, acquired.Texture);

        // Acquisition is synchronous, including when no semaphore was supplied;
        // completion retires readiness without consuming the binary signal.
        return ValueTask.FromResult(new SurfaceAcquireResult(status, frame));
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WGPU.wgpuSurfaceUnconfigure(_surface.Native.Handle);
        _disposed = true;
    }

    internal unsafe SurfaceStatus Present()
    {
        WGPUStatus status = WGPU.wgpuSurfacePresent(_surface.Native.Handle);
        if (status != WGPUStatus.Success)
        {
            throw new InvalidOperationException("wgpu presentation failed.");
        }

        return SurfaceStatus.Success;
    }

    private void ValidateAlive()
    {
        _surface.ValidateAlive();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
