using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserSurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly BrowserSwapchain _swapchain;
    private readonly object? _acquisitionOperation;
    private readonly BrowserTexture _texture;

    internal BrowserSurfaceFrame(BrowserSwapchain swapchain, JSObject native)
    {
        _swapchain = swapchain;
        _acquisitionOperation = Owner.Validation != null ? new object() : null;
        Lifetime = new(IsNativeReleased);
        SwapchainDesc desc = swapchain.Configuration;
        _texture = new(swapchain.Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, native, Lifetime);
        Owner.Validation?.Register(this, _swapchain);
        Owner.Validation?.Register(_texture, this);
        Owner.Validation?.SetTextureState(_texture, TextureState.Undefined);
    }

    public SurfaceFrameStatus Status => Lifetime.Status;

    public IGraphicsTexture Texture => _texture;

    internal object AcquisitionOperation => _acquisitionOperation!;

    internal SurfaceFrameLifetime Lifetime { get; }

    internal BrowserDevice Owner => _swapchain.Owner;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        Lifetime.ValidatePresent();
        IReadOnlyList<IGraphicsSemaphore> validationWaits = waitSemaphores ?? [];
        Owner.Validation?.ValidatePresentTexture(_texture, validationWaits);
        foreach (IGraphicsSemaphore value in validationWaits)
        {
            if (value is not BrowserSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner))
            {
                throw new ArgumentException("Presentation semaphore belongs to another device.", nameof(waitSemaphores));
            }
        }

        Owner.Validation?.PresentationWaitValidate(validationWaits);

        SurfaceStatus status = _swapchain.Present();
        if (status is SurfaceStatus.Success or SurfaceStatus.Suboptimal)
        {
            Owner.Validation?.PresentationWaited(this, validationWaits);
        }

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

        if (Owner.Validation != null)
        {
            if (!IsNativeReleased())
            {
                throw new InvalidOperationException("Validation: native presentation synchronization is still in flight.");
            }

            Owner.Validation.Release(_texture);
            Owner.Validation.Release(this);
        }

        _texture.DisposeLease();
        Lifetime.MarkDisposed();
    }

    private bool IsNativeReleased()
    {
        // WebGPU waits are logical ordering on the single queue. No native
        // presentation synchronization object is retained by this route.
        if (_acquisitionOperation != null)
        {
            Owner.Validation?.CompleteSemaphores(_acquisitionOperation);
        }

        Owner.Validation?.CompleteSemaphores(this);
        Owner.Validation?.Complete(this);
        return true;
    }
}
