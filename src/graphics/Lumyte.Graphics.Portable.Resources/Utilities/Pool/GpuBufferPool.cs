namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Owns whole buffers and lends them by exact size and usage description.</summary>
/// <remarks>
/// The backend is borrowed. The caller serializes pool operations in the backend's execution context,
/// ends all mapping, binding, recording and GPU uses before returning a lease, and never destroys its handle directly.
/// Reusing a buffer does not reset its contents or GPU state. No GPU wait or automatic retirement occurs.
/// </remarks>
public sealed class GpuBufferPool : IDisposable
{
    private readonly ResourcePool<GpuBufferDescription, GpuBufferHandle> pool;

    public GpuBufferPool(IPortableGpuBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        pool = new(backend.CreateBuffer, backend.DestroyBuffer, nameof(GpuBufferPool));
    }

    /// <summary>Lends an unused matching buffer or creates one using the unchanged description.</summary>
    public GpuBufferLease Acquire(GpuBufferDescription description)
        => pool.Acquire(description, static loan => new GpuBufferLease(loan));

    /// <summary>Returns this exact loan after the caller has ended all references and uses.</summary>
    public void Release(GpuBufferLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        pool.Release(lease.Loan);
    }

    /// <summary>Attempts to destroy every idle buffer once; active loans remain untouched.</summary>
    public void Trim() => pool.Trim();

    /// <summary>Rejects outstanding loans; otherwise attempts every idle destruction once without owning the backend.</summary>
    public void Dispose() => pool.Dispose();
}
