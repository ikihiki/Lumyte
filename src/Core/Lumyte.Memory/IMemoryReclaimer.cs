namespace Lumyte.Memory;

/// <summary>Releases unused allocations in response to an explicit request.</summary>
public interface IMemoryReclaimer
{
    /// <summary>Attempts to return unused charges from one pool.</summary>
    /// <param name="pool">The pool to reclaim.</param>
    /// <param name="targetBytes">The requested byte count.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The byte count reported by the reclaimer.</returns>
    ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default);
}
