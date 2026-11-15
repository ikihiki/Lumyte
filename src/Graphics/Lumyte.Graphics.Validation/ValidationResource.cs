namespace Lumyte.Graphics.Validation;

internal abstract class ValidationResource<T> : IValidationObject, IDisposable
    where T : class, IDisposable
{
    protected ValidationResource(ValidationDevice device, T raw, object parent, bool ownsNative = true)
    {
        Device = device;
        Raw = raw;
        OwnsNative = ownsNative;
        device.Register(this, raw, parent, ownsNative);
    }

    public ValidationDevice Device { get; }

    public object RawObject => Raw;

    public bool IsDisposed { get; private set; }

    internal T Raw { get; }

    protected bool OwnsNative { get; }

    public virtual void ValidateAlive()
    {
        Device.EnsureActive();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    public virtual void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Device.Tracker.ValidateRelease(this);
        DisposeNative();
        Device.Tracker.Release(this);
        Device.Forget(Raw, this);
        IsDisposed = true;
    }

    protected virtual void DisposeNative()
    {
        if (OwnsNative)
        {
            Raw.Dispose();
        }
    }
}
