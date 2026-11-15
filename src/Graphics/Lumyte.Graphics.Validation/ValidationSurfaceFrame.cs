using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationSurfaceFrame : ValidationResource<IGraphicsSurfaceFrame>, IGraphicsSurfaceFrame
{
    private readonly ValidationTexture _texture;
    private readonly object _acquisition;
    private bool _released;
    private bool _unknownPresent;

    internal ValidationSurfaceFrame(ValidationDevice device, IGraphicsSurfaceFrame raw, ValidationSwapchain swapchain, IGraphicsSemaphore? signal, object acquisition)
        : base(device, raw, swapchain)
    {
        _acquisition = acquisition;
        _texture = new ValidationTexture(device, raw.Texture, this, false);
        try
        {
            device.Tracker.SetTextureState(_texture, TextureState.Undefined);
            device.Tracker.AcquireTexture(_acquisition, _texture, signal);
        }
        catch
        {
            device.Tracker.Release(_texture);
            throw;
        }
    }

    public SurfaceFrameStatus Status => IsDisposed ? SurfaceFrameStatus.Disposed : Raw.Status;

    public IGraphicsTexture Texture
    {
        get
        {
            ValidateAlive();
            return _texture;
        }
    }

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        ValidateAlive();
        if (Status != SurfaceFrameStatus.Acquired)
        {
            throw new InvalidOperationException("Present an acquired frame once.");
        }

        if (_unknownPresent)
        {
            throw new InvalidOperationException("Validation: previous native presentation effects are unknown.");
        }

        IGraphicsSemaphore[] waits = waitSemaphores?.ToArray() ?? [];
        IGraphicsSemaphore[] native = waits.Select(Device.Unwrap).ToArray();
        Device.Tracker.ValidatePresentTexture(_texture, waits);
        Device.Tracker.PresentationWaitValidate(waits);
        SurfaceStatus result;
        _released = false;
        try
        {
            result = Raw.Present(native);
        }
        catch
        {
            _unknownPresent = true;
            Device.Tracker.MarkPartialSubmissionFailure();
            foreach (IGraphicsSemaphore? wait in waits)
            {
                Device.Tracker.Use(this, wait);
            }

            Device.Tracker.Use(this, _texture);
            throw;
        }

        _released = false;
        if (result is SurfaceStatus.Success or SurfaceStatus.Suboptimal)
        {
            Device.Tracker.PresentationWaited(this, waits);
        }
        else
        {
            // A recovery status does not prove whether native waits consumed
            // signals. Retain the operands and quarantine future queue work.
            _unknownPresent = true;
            Device.Tracker.MarkPartialSubmissionFailure();
            foreach (IGraphicsSemaphore? wait in waits)
            {
                Device.Tracker.Use(this, wait);
            }
        }

        Device.Tracker.Use(this, _texture);
        return result;
    }

    public async ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        await Raw.WaitForReleaseAsync(cancellationToken).ConfigureAwait(false);
        Device.Tracker.CompleteSemaphores(_acquisition);
        Device.Tracker.CompleteSemaphores(this);
        Device.Tracker.Complete(this, !_unknownPresent);
        _released = true;
    }

    public override void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!_released)
        {
            throw new InvalidOperationException("Validation: explicitly observe native frame release before disposal.");
        }

        Device.Tracker.ValidateRelease(_texture);
        Raw.Dispose();
        _texture.ReleaseFrameImage();
        base.Dispose();
    }

    protected override void DisposeNative()
    {
    }
}
