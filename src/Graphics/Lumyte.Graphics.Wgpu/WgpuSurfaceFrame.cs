using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly WgpuSwapchain _swapchain;
    private readonly WgpuTexture _texture;

    internal WgpuSurfaceFrame(WgpuSwapchain swapchain, Ahjo.Wgpu.Texture native)
    {
        _swapchain = swapchain;
        Lifetime = new(IsNativeReleased);
        SwapchainDesc desc = swapchain.Configuration;
        _texture = new(swapchain.Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, native, Lifetime);
    }

    public SurfaceFrameStatus Status => Lifetime.Status;

    public IGraphicsTexture Texture => _texture;

    internal SurfaceFrameLifetime Lifetime { get; }

    internal WgpuDevice Owner => _swapchain.Owner;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        Lifetime.ValidatePresent();
        IReadOnlyList<IGraphicsSemaphore> validationWaits = waitSemaphores ?? [];
        foreach (IGraphicsSemaphore value in validationWaits)
        {
            if (value is not WgpuSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner))
            {
                throw new ArgumentException("Presentation semaphore belongs to another device.", nameof(waitSemaphores));
            }
        }

        SurfaceStatus status = _swapchain.Present();

        Lifetime.MarkPresented();
        return status;
    }

    public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => Lifetime.WaitForReleaseAsync(cancellationToken);

    public void Dispose()
    {
        if (Status == SurfaceFrameStatus.Disposed)
        {
            return;
        }

        if (!IsNativeReleased())
        {
            throw new InvalidOperationException("Native presentation synchronization is still in flight.");
        }

        _texture.DisposeLease();
        Lifetime.MarkDisposed();
    }

    private bool IsNativeReleased() =>

        // The surface-present call retains no caller semaphore: WebGPU binary
        // semaphores model ordering on the device's single queue only.
        true;
}
