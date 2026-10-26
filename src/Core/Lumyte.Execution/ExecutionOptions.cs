namespace Lumyte.Execution;

/// <summary>Capacity limits for retained commands and results, including unobserved results.</summary>
public sealed class ExecutionOptions
{
    /// <summary>Gets the normal run limit.</summary>
    public int MaxInFlightRuns { get; init; } = 64;

    /// <summary>Gets the normal command limit.</summary>
    public int MaxCommands { get; init; } = 4096;

    /// <summary>Gets the graph node and edge limit.</summary>
    public int MaxGraphNodes { get; init; } = 4096;

    /// <summary>Gets the retained typed payload and result budget.</summary>
    public long MaxPayloadBytes { get; init; } = 4 * 1024 * 1024;

    /// <summary>Gets the independent lifecycle cleanup command limit.</summary>
    public int ReservedCleanupCommands { get; init; } = 32;

    /// <summary>Gets the independent lifecycle cleanup run limit.</summary>
    public int ReservedCleanupRuns { get; init; } = 4;

    /// <summary>Gets the independent lifecycle cleanup payload budget.</summary>
    public long ReservedCleanupPayloadBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInFlightRuns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCommands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxGraphNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReservedCleanupCommands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReservedCleanupRuns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ReservedCleanupPayloadBytes);
    }
}
