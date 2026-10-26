using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Adapts one exact resource generation to a renderer and independently retained prepared batches.</summary>
/// <typeparam name="TResource">The resource owning the program.</typeparam>
/// <remarks>Disposal returns the renderer's ownership. Issued program leases keep the resource alive until GPU use ends.</remarks>
public sealed class RetainedGraphicsProgramProvider<TResource> : IGraphicsProgramProvider, IDisposable
    where TResource : notnull
{
    private readonly object _gate = new();
    private readonly ResourceLease<TResource> _lease;
    private readonly Func<TResource, GraphicsProgramResource> _select;
    private readonly string _programId;
    private int _owners = 1;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RetainedGraphicsProgramProvider{TResource}"/> class, taking ownership of the lease.</summary>
    /// <param name="lease">The transferred resource lease.</param>
    /// <param name="select">Selects the borrowed program from that resource.</param>
    /// <param name="programId">The consumer's shader ABI identifier.</param>
    public RetainedGraphicsProgramProvider(ResourceLease<TResource> lease, Func<TResource, GraphicsProgramResource> select, string programId)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentException.ThrowIfNullOrWhiteSpace(programId);
        (_lease, _select, _programId) = (lease, select, programId);
    }

    /// <inheritdoc/>
    public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!string.Equals(_programId, programId, StringComparison.Ordinal))
            {
                throw new NotSupportedException("The resource does not supply the requested shader ABI.");
            }

            GraphicsProgramResources resources = _select(_lease.Value).Resources;
            if (!ReferenceEquals(device, resources.Device))
            {
                throw new ArgumentException("The program belongs to a different physical device.", nameof(device));
            }

            var result = new GraphicsProgramLease(resources, Release, () => _ = _lease.Value);
            _owners++;
            return result;
        }
    }

    /// <summary>Returns the renderer's ownership without returning leases retained by prepared draws.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Release();
        }
    }

    private void Release()
    {
        lock (_gate)
        {
            if (--_owners == 0)
            {
                _lease.Dispose();
            }
        }
    }
}
