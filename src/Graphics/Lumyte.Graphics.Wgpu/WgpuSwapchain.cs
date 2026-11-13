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
        Owner.Validation?.Register(this, surface);
    }

    public SwapchainDesc Configuration { get; private set; }

    internal WgpuDevice Owner => _surface.Owner;

    public void Reconfigure(SwapchainDesc desc)
    {
        ValidateAlive();
        Owner.Validation?.Release(this);
        Owner.Validation?.Register(this, _surface);
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

        if (signalSemaphore != null)
        {
            Owner.Validation?.PresentationSignalValidate(signalSemaphore);
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
        Owner.Validation?.AcquireTexture(frame.AcquisitionOperation, frame.Texture, signalSemaphore);
        if (signalSemaphore != null)
        {
            Owner.Validation?.PresentationSignaled(frame.AcquisitionOperation, signalSemaphore);
        }

        // Acquisition is synchronous, including when no semaphore was supplied;
        // completion retires readiness without consuming the binary signal.
        Owner.Validation?.CompleteSemaphores(frame.AcquisitionOperation);

        return ValueTask.FromResult(new SurfaceAcquireResult(status, frame));
    }

    public unsafe void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Owner.Validation?.Release(this);
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
