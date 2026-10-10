using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly BrowserSwapchain _swapchain;
    private readonly BrowserTexture _texture;

    internal BrowserSurfaceFrame(BrowserSwapchain swapchain, JSObject native)
    {
        _swapchain = swapchain;
        SwapchainDesc desc = swapchain.Configuration;
        _texture = new(swapchain.Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, native, Lifetime);
    }

    public SurfaceFrameStatus Status => Lifetime.Status;

    public IGraphicsTexture Texture => _texture;

    internal SurfaceFrameLifetime Lifetime { get; } = new(() => true);

    internal BrowserDevice Owner => _swapchain.Owner;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        Lifetime.ValidatePresent();
        IGraphicsSemaphore[] snapshot = waitSemaphores?.ToArray() ?? [];
        SemaphoreValidation.Presentation(snapshot);
        BrowserSemaphore[] waits = snapshot.Select(value =>
        {
            if (value is not BrowserSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner))
            {
                throw new ArgumentException("Presentation semaphore belongs to another device.", nameof(waitSemaphores));
            }

            semaphore.State.ValidateWait();
            return semaphore;
        }).ToArray();
        Lifetime.MarkPresented();
        SurfaceStatus status = _swapchain.Present();
        foreach (BrowserSemaphore semaphore in waits)
        {
            semaphore.State.MarkWait(() => Lifetime.IsReleased);
        }

        return status;
    }

    public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => Lifetime.WaitForReleaseAsync(cancellationToken);

    public void Dispose()
    {
        if (Status == SurfaceFrameStatus.Disposed)
        {
            return;
        }

        Lifetime.ValidateRelease();
        _texture.DisposeLease();
        Lifetime.MarkDisposed();
        _swapchain.ReleaseFrame(this);
    }
}
