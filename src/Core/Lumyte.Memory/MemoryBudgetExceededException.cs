namespace Lumyte.Memory;

/// <summary>Indicates that a new reservation would exceed a pool's hard limit.</summary>
/// <param name="pool">The rejected pool.</param>
/// <param name="requestedBytes">The rejected byte count.</param>
/// <param name="snapshot">The pool state observed during rejection.</param>
public sealed class MemoryBudgetExceededException(MemoryPoolId pool, ulong requestedBytes, MemorySnapshot snapshot)
    : InvalidOperationException($"Reserving {requestedBytes} bytes would exceed memory pool '{pool}' hard limit of {snapshot.HardLimitBytes} bytes.")
{
    /// <summary>Gets the rejected pool.</summary>
    public MemoryPoolId Pool { get; } = pool;

    /// <summary>Gets the rejected byte count.</summary>
    public ulong RequestedBytes { get; } = requestedBytes;

    /// <summary>Gets the pool state observed while rejecting the reservation.</summary>
    public MemorySnapshot Snapshot { get; } = snapshot;
}
