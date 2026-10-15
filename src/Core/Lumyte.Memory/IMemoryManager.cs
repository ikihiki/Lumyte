namespace Lumyte.Memory;

/// <summary>Creates scopes and coordinates shared memory budgets.</summary>
public interface IMemoryManager : IAsyncDisposable
{
    /// <summary>Creates an independently owned runtime scope.</summary>
    /// <param name="owner">The diagnostic owner.</param>
    /// <returns>The owned scope.</returns>
    IMemoryScope CreateScope(string owner);

    /// <summary>Gets an immutable snapshot of one pool.</summary>
    /// <param name="pool">The inspected pool.</param>
    /// <returns>The current snapshot.</returns>
    MemorySnapshot GetSnapshot(MemoryPoolId pool);

    /// <summary>Registers a borrowed reclaimer until the returned token is disposed.</summary>
    /// <param name="reclaimer">The borrowed reclaimer.</param>
    /// <returns>The registration token.</returns>
    IDisposable RegisterReclaimer(IMemoryReclaimer reclaimer);

    /// <summary>Explicitly requests reclamation.</summary>
    /// <param name="pool">The pool to reclaim.</param>
    /// <param name="targetBytes">The requested byte count.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The bytes actually removed from this manager's ledger.</returns>
    ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default);
}
