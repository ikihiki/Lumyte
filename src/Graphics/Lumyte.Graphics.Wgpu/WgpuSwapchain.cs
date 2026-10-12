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
