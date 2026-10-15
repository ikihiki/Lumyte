namespace Lumyte.Memory;

/// <summary>Defines a soft and hard limit for one pool.</summary>
/// <param name="SoftLimitBytes">The threshold at which callers should consider reclamation.</param>
/// <param name="HardLimitBytes">The maximum tracked bytes accepted by new reservations.</param>
public readonly record struct MemoryBudget(ulong SoftLimitBytes, ulong HardLimitBytes)
{
    /// <summary>Gets an unlimited budget.</summary>
    public static MemoryBudget Unlimited { get; } = new(ulong.MaxValue, ulong.MaxValue);
}
