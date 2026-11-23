using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal abstract class ValidationResource<T> : IValidationObject, IDisposable, IGraphicsNativeOrigin
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

    IGraphicDevice IGraphicsNativeOrigin.Device => Device;

    object IGraphicsNativeOrigin.AllocationIdentity => NativeOrigin.AllocationIdentity;

    public object RawObject => Raw;

    public bool IsDisposed { get; private set; }

    internal T Raw { get; }

    protected bool OwnsNative { get; }

    private IGraphicsNativeOrigin NativeOrigin => Raw as IGraphicsNativeOrigin
        ?? throw new NotSupportedException("The wrapped backend does not expose native resource provenance.");

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

    void IGraphicsNativeOrigin.ValidateOrigin()
    {
        ValidateAlive();
        NativeOrigin.ValidateOrigin();
    }

    protected virtual void DisposeNative()
    {
        if (OwnsNative)
        {
            Raw.Dispose();
        }
    }
}
