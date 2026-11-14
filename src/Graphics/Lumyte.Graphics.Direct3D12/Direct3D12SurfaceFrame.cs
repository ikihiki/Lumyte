using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12SurfaceFrame : IGraphicsSurfaceFrame
{
    private readonly Direct3D12Swapchain _swapchain;
    private readonly object? _acquisitionOperation;
    private readonly Direct3D12Texture _texture;
    private readonly nint _fence;
    private readonly SurfaceFrameLifetime _lifetime;
    private readonly nint _acquireFence;
    private bool _acquireSignalPending;
    private bool _acquireTracked;
    private bool _releaseSignalPending;
    private bool _releasePending;

    internal Direct3D12SurfaceFrame(Direct3D12Swapchain swapchain, nint texture)
    {
        _swapchain = swapchain;
        _acquisitionOperation = _swapchain.Owner.Validation != null ? new object() : null;
        swapchain.Owner.Check(NativeMethods.FenceCreate(swapchain.Owner.Native, out _fence));
        try
        {
            if (_acquisitionOperation != null)
            {
                swapchain.Owner.Check(NativeMethods.FenceCreate(swapchain.Owner.Native, out _acquireFence));
            }
        }
        catch
        {
            NativeMethods.Release(_fence);
            throw;
        }

        _lifetime = new(IsNativeReleased);
        SwapchainDesc desc = swapchain.Configuration;
        _texture = new(swapchain.Owner, new() { Width = desc.Width, Height = desc.Height, Format = desc.Format, Usage = desc.Usage }, texture, _lifetime);
        _swapchain.Owner.Validation?.Register(this, _swapchain);
        _swapchain.Owner.Validation?.Register(_texture, this);

        // The abstract acquisition contract discards prior contents. D3D enhanced
        // barriers map Undefined to UNDEFINED + DISCARD, valid for a backbuffer

        // acquired natively in PRESENT/COMMON; no implicit transition is inserted.
        _swapchain.Owner.Validation?.SetTextureState(_texture, TextureState.Undefined);
    }

    public SurfaceFrameStatus Status => _lifetime.Status;

    public IGraphicsTexture Texture => _texture;

    internal object AcquisitionOperation => _acquisitionOperation!;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        _lifetime.ValidatePresent();
        if (_releasePending)
        {
            throw new InvalidOperationException("A previous presentation attempt issued native waits; release this frame before retrying.");
        }

        IReadOnlyList<IGraphicsSemaphore> validationWaits = waitSemaphores ?? [];
        _swapchain.Owner.Validation?.ValidatePresentTexture(_texture, validationWaits);
        _swapchain.Owner.Validation?.PresentationWaitValidate(validationWaits);

        var waits = new Direct3D12Semaphore[validationWaits.Count];
        int waitIndex = 0;
        foreach (IGraphicsSemaphore value in waitSemaphores ?? [])
        {
            if (value is not Direct3D12Semaphore semaphore || !ReferenceEquals(semaphore.Owner, _swapchain.Owner))
            {
                throw new ArgumentException("Presentation waits must be unique same-device semaphores.");
            }

            _ = semaphore.Native;
            semaphore.ValidateWait();
            waits[waitIndex++] = semaphore;
        }

        SurfaceStatus status;
        List<IGraphicsSemaphore>? issuedWaits = _swapchain.Owner.Validation == null ? null : [];
        bool publishedWaits = false;
        try
        {
            foreach (Direct3D12Semaphore semaphore in waits)
            {
                semaphore.Wait();
                _releasePending = true;
                issuedWaits?.Add(semaphore);
            }

            _swapchain.Owner.Validation?.PresentationWaited(this, issuedWaits!);
            publishedWaits = true;
            status = _swapchain.Present();
        }
        catch
        {
            // Queue waits already issued still need an actual completion fence.
            if (_releasePending)
            {
                if (!publishedWaits)
                {
                    _swapchain.Owner.Validation?.PresentationWaited(this, issuedWaits!);
                }

                SignalReleaseCompletion();
            }

            throw;
        }

        _lifetime.MarkPresented();
        _releasePending = true;
        _swapchain.Owner.Validation?.Use(this, _texture);
        SignalReleaseCompletion();
        return status;
    }

    public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default) => _lifetime.WaitForReleaseAsync(cancellationToken);

    public void Dispose()
    {
        if (Status == SurfaceFrameStatus.Disposed)
        {
            return;
        }

        if (_swapchain.Owner.Validation != null)
        {
            if (!IsNativeReleased())
            {
                throw new InvalidOperationException("Validation: native presentation synchronization is still in flight.");
            }

            _swapchain.Owner.Validation.Release(_texture);
            _swapchain.Owner.Validation.Release(this);
        }

        _texture.DisposeLease();
        NativeMethods.Release(_acquireFence);
        NativeMethods.Release(_fence);
        _lifetime.MarkDisposed();
        _swapchain.ReleaseFrame();
    }

    internal void TrackAcquireSignal()
    {
        // Retain the native signal even when issuing its completion fence fails.
        _acquireTracked = true;
        _acquireSignalPending = true;
        SignalAcquireCompletion();
    }

    private void SignalAcquireCompletion()
    {
        _swapchain.Owner.Check(NativeMethods.QueueSignal(_swapchain.Owner.Native, _acquireFence));
        _acquireSignalPending = false;
    }

    private void SignalReleaseCompletion()
    {
        _releaseSignalPending = true;
        _swapchain.Owner.Check(NativeMethods.QueueSignal(_swapchain.Owner.Native, _fence));
        _releaseSignalPending = false;
    }

    private bool IsNativeReleased()
    {
        if (_swapchain.Owner.Status == GraphicsDeviceStatus.Ready)
        {
            if (_acquireSignalPending)
            {
                SignalAcquireCompletion();
            }

            if (_releaseSignalPending)
            {
                SignalReleaseCompletion();
            }
        }

        if (_swapchain.Owner.Status != GraphicsDeviceStatus.Lost &&
            (_acquireSignalPending || _releaseSignalPending ||
             (_acquireTracked && NativeMethods.FenceCompleted(_acquireFence) < 1) ||
             (_releasePending && NativeMethods.FenceCompleted(_fence) < 1)))
        {
            return false;
        }

        if (_acquisitionOperation != null)
        {
            _swapchain.Owner.Validation?.CompleteSemaphores(_acquisitionOperation);
        }

        _swapchain.Owner.Validation?.CompleteSemaphores(this);
        _swapchain.Owner.Validation?.Complete(this);
        return true;
    }
}
