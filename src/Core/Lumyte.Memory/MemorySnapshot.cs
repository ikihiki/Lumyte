namespace Lumyte.Memory;

/// <summary>Contains an immutable snapshot of one pool's tracked usage and budget.</summary>
public sealed record MemorySnapshot
{
    /// <summary>Gets total committed bytes, including estimates and the full capacity of each live CPU chunk once.</summary>
    public required ulong CommittedBytes { get; init; }

    /// <summary>Gets the total uncommitted reserved bytes.</summary>
    public required ulong ReservedBytes { get; init; }

    /// <summary>Gets the estimated subset of <see cref="CommittedBytes"/>.</summary>
    public required ulong EstimatedBytes { get; init; }

    /// <summary>Gets the soft budget threshold.</summary>
    public required ulong SoftLimitBytes { get; init; }

    /// <summary>Gets the hard reservation limit.</summary>
    public required ulong HardLimitBytes { get; init; }

    /// <summary>Gets a value indicating whether committed and reserved bytes exceed the hard limit.</summary>
    public required bool IsOverBudget { get; init; }
}
