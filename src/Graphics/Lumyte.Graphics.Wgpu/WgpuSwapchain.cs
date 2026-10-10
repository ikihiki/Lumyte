using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSwapchain : IGraphicsSwapchain
{
    private readonly WgpuSurface _surface;
    private int _frameCount;
    private WgpuSurfaceFrame? _active;
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
            throw new InvalidOperationException("Submit and present or discard the active wgpu image before acquisition.");
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

        _active = new(this, acquired.Texture);
        _frameCount++;
        return ValueTask.FromResult(new SurfaceAcquireResult(status, _active));
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_frameCount != 0)
        {
            throw new InvalidOperationException("Release all frames before swapchain disposal.");
        }

        WGPU.wgpuSurfaceUnconfigure(_surface.Native.Handle);
        _disposed = true;
        _surface.ReleaseSwapchain();
    }

    internal unsafe SurfaceStatus Present()
    {
        WGPUStatus status = WGPU.wgpuSurfacePresent(_surface.Native.Handle);
        _active = null;
        if (status != WGPUStatus.Success)
        {
            throw new InvalidOperationException("wgpu presentation failed.");
        }

        return SurfaceStatus.Success;
    }

    internal void ReleaseFrame(WgpuSurfaceFrame frame)
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
