namespace Lumyte.Graphics.Wgpu;

internal abstract class GpuResource : IDisposable
{
    private bool _disposed;
    private int _leases;

    internal GpuResource(WgpuDevice owner)
    {
        Owner = owner;
        owner.ResourceCount++;
    }

    internal WgpuDevice Owner { get; }

    protected int ActiveLeases => _leases;

    public void Dispose()
    {
        lock (Owner.Gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_leases != 0)
            {
                throw new InvalidOperationException("Resource is referenced by recorded or in-flight GPU work.");
            }

            ReleaseNative();
            _disposed = true;
            Owner.ResourceCount--;
        }
    }

    internal void Check(WgpuDevice owner)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(owner, Owner))
        {
            throw new ArgumentException("Resource belongs to another device.");
        }

        owner.Check();
    }

    internal void Acquire()
    {
        Check(Owner);
        _leases++;
    }

    internal virtual void ReleaseLease() => _leases--;

    internal void RequireIdle()
    {
        Check(Owner);
        if (_leases != 0)
        {
            throw new InvalidOperationException("Resource is referenced by recorded or in-flight GPU work.");
        }
    }

    protected abstract void ReleaseNative();
}
