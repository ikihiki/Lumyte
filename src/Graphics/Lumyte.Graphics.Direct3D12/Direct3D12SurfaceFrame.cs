using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12SurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly Direct3D12Swapchain _swapchain;
    private readonly Direct3D12Texture _texture;
    private readonly nint _fence;
    private readonly SurfaceFrameLifetime _lifetime;
    private bool _presented;

    internal Direct3D12SurfaceFrame(Direct3D12Swapchain swapchain, nint texture)
    {
        _swapchain = swapchain;
        swapchain.Owner.Check(NativeMethods.FenceCreate(swapchain.Owner.Native, out _fence));
        _lifetime = new(() => !_presented || NativeMethods.FenceCompleted(_fence) >= 1);
        SwapchainDesc desc = swapchain.Configuration;
        _texture = new(swapchain.Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, texture, _lifetime);
    }

    public SurfaceFrameStatus Status => _lifetime.Status;

    public IGraphicsTexture Texture => _texture;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        _lifetime.ValidatePresent();
        var unique = new HashSet<Direct3D12Semaphore>();
        foreach (IGraphicsSemaphore value in waitSemaphores ?? [])
        {
            if (value is not Direct3D12Semaphore semaphore || !ReferenceEquals(semaphore.Owner, _swapchain.Owner) || !unique.Add(semaphore))
            {
                throw new ArgumentException("Presentation waits must be unique same-device semaphores.");
            }

            _ = semaphore.Native;
            semaphore.ValidateWait();
        }

        foreach (Direct3D12Semaphore semaphore in unique)
        {
            semaphore.Wait();
        }

        SurfaceStatus status = _swapchain.Present();
        _lifetime.MarkPresented();
        _presented = true;
        _swapchain.Owner.Check(NativeMethods.QueueSignal(_swapchain.Owner.Native, _fence));
        return status;
    }

    public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => _lifetime.WaitForReleaseAsync(cancellationToken);

    public void Dispose()
    {
        if (Status == SurfaceFrameStatus.Disposed)
        {
            return;
        }

        _texture.DisposeLease();
        NativeMethods.Release(_fence);
        _lifetime.MarkDisposed();
        _swapchain.ReleaseFrame();
    }
}
