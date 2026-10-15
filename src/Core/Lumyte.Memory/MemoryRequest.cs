namespace Lumyte.Memory;

/// <summary>Describes a memory reservation request.</summary>
public sealed record MemoryRequest
{
    /// <summary>Gets the budget pool.</summary>
    public required MemoryPoolId Pool { get; init; }

    /// <summary>Gets the positive byte count to reserve.</summary>
    public required ulong Bytes { get; init; }

    /// <summary>Gets the reason the allocation is retained.</summary>
    public required MemoryPurpose Purpose { get; init; }

    /// <summary>Gets optional allocation diagnostic information.</summary>
    public string? Label { get; init; }
}
