using System.Runtime.ExceptionServices;

namespace Lumyte.Graphics.Portable.Resources;

internal sealed class ResourcePool<TDescription, THandle>(
    Func<TDescription, THandle> create, Action<THandle> destroy, string objectName)
    where TDescription : struct
    where THandle : class
{
    private readonly Dictionary<TDescription, Stack<THandle>> idle = [];
    private int activeCount;
    private bool disposed;

    internal TLease Acquire<TLease>(TDescription description, Func<Lease, TLease> wrap)
    {
        RequireAvailable();
        int nextCount = checked(activeCount + 1);
        TLease loan;
        if (idle.TryGetValue(description, out Stack<THandle>? available))
        {
            loan = wrap(new(this, description, available.Peek()));
            available.Pop();
            if (available.Count == 0) { idle.Remove(description); }
        }
        else
        {
            THandle resource = create(description);
            try { loan = wrap(new(this, description, resource)); }
            catch (Exception error)
            {
                try { destroy(resource); }
                catch (Exception cleanupError)
                { throw new AggregateException("Creating a resource loan and releasing its resource failed.", error, cleanupError); }
                throw;
            }
        }
        activeCount = nextCount;
        return loan;
    }

    internal void Release(Lease lease)
    {
        RequireAvailable();
        if (!ReferenceEquals(lease.Owner, this))
        { throw new ArgumentException("The lease belongs to another resource pool.", nameof(lease)); }
        if (lease.Returned)
        { throw new ArgumentException("This resource lease has already been returned.", nameof(lease)); }

        if (!idle.TryGetValue(lease.Description, out Stack<THandle>? available))
        {
            available = new();
            available.Push(lease.Resource);
            idle.Add(lease.Description, available);
        }
        else { available.Push(lease.Resource); }
        lease.Returned = true;
        activeCount--;
    }

    internal void Trim()
    {
        RequireAvailable();
        DestroyAll(DetachIdle());
    }

    internal void Dispose()
    {
        if (disposed) { return; }
        if (activeCount != 0)
        { throw new InvalidOperationException("Return all resource leases before disposing the pool."); }
        THandle[] resources = DetachIdle();
        disposed = true;
        DestroyAll(resources);
    }

    private THandle[] DetachIdle()
    {
        THandle[] resources = idle.Values.SelectMany(static handles => handles).ToArray();
        idle.Clear();
        return resources;
    }

    private void DestroyAll(THandle[] resources)
    {
        List<Exception>? errors = null;
        foreach (THandle resource in resources)
        {
            try { destroy(resource); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        if (errors is { Count: 1 }) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors is not null) { throw new AggregateException("Releasing pooled resources failed.", errors); }
    }

    private void RequireAvailable()
    {
        if (disposed) { throw new ObjectDisposedException(objectName); }
    }

    internal sealed class Lease(ResourcePool<TDescription, THandle> owner, TDescription description, THandle resource)
    {
        internal ResourcePool<TDescription, THandle> Owner { get; } = owner;
        internal TDescription Description { get; } = description;
        internal THandle Resource { get; } = resource;
        internal bool Returned { get; set; }

        internal THandle GetHandle()
        {
            if (Returned) { throw new InvalidOperationException("This resource lease has already been returned."); }
            return Resource;
        }
    }
}
