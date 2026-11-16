using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationSwapchain : ValidationResource<IGraphicsSwapchain>, IGraphicsSwapchain
{
    private readonly ValidationSurface _surface;
    private bool _unknownAcquire;
    private IGraphicsSurfaceFrame? _failedFrame;
    private object? _failedAcquisition;

    internal ValidationSwapchain(ValidationDevice device, IGraphicsSwapchain raw, ValidationSurface surface)
        : base(device, raw, surface)
    {
        _surface = surface;
    }

    public SwapchainDesc Configuration
    {
        get
        {
            ValidateAlive();
            return Raw.Configuration;
        }
    }

    public override void ValidateAlive()
    {
        base.ValidateAlive();
        _surface.ValidateAlive();
    }

    public void Reconfigure(SwapchainDesc desc)
    {
        ValidateAlive();
        if (_unknownAcquire)
        {
            throw new InvalidOperationException("Validation: acquisition effects are unknown.");
        }

        Device.Tracker.ValidateRelease(this);
        Raw.Reconfigure(desc);
    }

    public async ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        cancellationToken.ThrowIfCancellationRequested();
        if (_unknownAcquire)
        {
            throw new InvalidOperationException("Validation: previous acquisition effects are unknown.");
        }

        IGraphicsSemaphore? signal = signalSemaphore == null ? null : Device.Unwrap(signalSemaphore);
        if (signalSemaphore != null)
        {
            Device.Tracker.PresentationSignalValidate(signalSemaphore);
        }

        SurfaceAcquireResult result;
        try
        {
            result = await Raw.AcquireNextFrameAsync(signal, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _unknownAcquire = true;
            Device.Tracker.MarkPartialSubmissionFailure();
            Device.Tracker.Use(this, this);
            if (signalSemaphore != null)
            {
                Device.Tracker.Use(this, signalSemaphore);
            }

            throw;
        }

        if (result.Status is not (SurfaceStatus.Success or SurfaceStatus.Suboptimal))
        {
            return result with { Frame = null };
        }

        object acquisition = new();
        if (signalSemaphore != null)
        {
            // Native success issued the binary signal independently of wrapping.
            Device.Tracker.PresentationSignaled(acquisition, signalSemaphore);
        }

        if (result.Frame == null)
        {
            RetainFailedAcquisition(null, acquisition, signalSemaphore);
            throw new InvalidOperationException("Validation: successful native acquisition returned no frame.");
        }

        bool cleanupFailed = false;
        try
        {
            return result with
            {
                Frame = Device.OwnCreated(
                    result.Frame,
                    raw => new ValidationSurfaceFrame(Device, raw, this, signalSemaphore, acquisition),
                    raw =>
                    {
                        cleanupFailed = true;
                        RetainFailedAcquisition(raw, acquisition, signalSemaphore);
                    }),
            };
        }
        catch
        {
            if (!cleanupFailed)
            {
                // Native lease disposal retires its use, not its binary signal.
                Device.Tracker.CompleteSemaphores(acquisition);
            }

            throw;
        }
    }

    internal void RetireUnknownAfterDeviceLoss()
    {
        _failedFrame?.Dispose();
        _failedFrame = null;
        if (_failedAcquisition != null)
        {
            Device.Tracker.CompleteSemaphores(_failedAcquisition);
            _failedAcquisition = null;
        }

        Device.Tracker.Complete(this, false);
        _unknownAcquire = false;
    }

    protected override void DisposeNative()
    {
        if (_unknownAcquire)
        {
            throw new InvalidOperationException("Validation: unknown native acquisition cannot be retired through this interface.");
        }

        Raw.Dispose();
    }

    private void RetainFailedAcquisition(IGraphicsSurfaceFrame? raw, object acquisition, IGraphicsSemaphore? signal)
    {
        _failedFrame = raw;
        _failedAcquisition = acquisition;
        _unknownAcquire = true;
        Device.Tracker.MarkPartialSubmissionFailure();
        Device.Tracker.Use(this, this);
        if (signal != null)
        {
            Device.Tracker.Use(this, signal);
        }
    }
}
