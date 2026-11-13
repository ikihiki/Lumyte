using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuSurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly WgpuSwapchain _swapchain;
    private readonly object? _acquisitionOperation;
    private readonly WgpuTexture _texture;

    internal WgpuSurfaceFrame(WgpuSwapchain swapchain, Ahjo.Wgpu.Texture native)
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

    internal WgpuDevice Owner => _swapchain.Owner;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        Lifetime.ValidatePresent();
        IReadOnlyList<IGraphicsSemaphore> validationWaits = waitSemaphores ?? [];
        Owner.Validation?.ValidatePresentTexture(_texture, validationWaits);
        foreach (IGraphicsSemaphore value in validationWaits)
        {
            if (value is not WgpuSemaphore semaphore || !ReferenceEquals(semaphore.Owner, Owner))
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
        // The surface-present call retains no caller semaphore: WebGPU binary
        // semaphores model ordering on the device's single queue only.
        if (_acquisitionOperation != null)
        {
            Owner.Validation?.CompleteSemaphores(_acquisitionOperation);
        }

        Owner.Validation?.CompleteSemaphores(this);
        Owner.Validation?.Complete(this);
        return true;
    }
}
