using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Resolves preloaded program handles from one caller-owned publication snapshot without I/O.</summary>
public sealed class ResourceGraphicsProgramProvider : IGraphicsProgramProvider
{
    private readonly ResourceSnapshot _snapshot;
    private readonly Dictionary<string, ResourceHandle<GraphicsProgramResource>> _programs;

    /// <summary>Initializes a new instance of the <see cref="ResourceGraphicsProgramProvider"/> class.</summary>
    /// <param name="snapshot">The borrowed snapshot, retained until drawing preparation completes.</param>
    /// <param name="programs">The ABI identifiers and their preloaded logical handles.</param>
    public ResourceGraphicsProgramProvider(ResourceSnapshot snapshot, IReadOnlyDictionary<string, ResourceHandle<GraphicsProgramResource>> programs)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ArgumentNullException.ThrowIfNull(programs);
        _programs = new(programs, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!_programs.TryGetValue(programId, out ResourceHandle<GraphicsProgramResource> handle))
        {
            throw new NotSupportedException("No resource handle is configured for this program ABI.");
        }

        if (!_snapshot.TryAcquire(handle, out ResourceLease<GraphicsProgramResource>? lease))
        {
            throw new InvalidOperationException("The program is not available in this publication snapshot.");
        }

        try
        {
            GraphicsProgramResources resources = lease!.Value.Resources;
            if (!ReferenceEquals(resources.Device, device))
            {
                throw new ArgumentException("The program belongs to a different physical device.", nameof(device));
            }

            return new(resources, lease.Dispose, () => _ = lease.Value);
        }
        catch
        {
            lease!.Dispose();
            throw;
        }
    }
}
