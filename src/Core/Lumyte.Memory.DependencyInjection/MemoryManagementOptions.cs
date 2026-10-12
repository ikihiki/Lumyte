namespace Lumyte.Memory.DependencyInjection;

/// <summary>Collects immutable pool budgets during dependency injection configuration.</summary>
public sealed class MemoryManagementOptions
{
    private readonly Dictionary<MemoryPoolId, MemoryBudget> _budgets = [];

    internal IReadOnlyDictionary<MemoryPoolId, MemoryBudget> Budgets => _budgets;

    /// <summary>Sets the soft and hard limits for one pool.</summary>
    /// <param name="pool">The configured pool.</param>
    /// <param name="softLimitBytes">The reclamation threshold.</param>
    /// <param name="hardLimitBytes">The reservation limit.</param>
    public void SetBudget(MemoryPoolId pool, ulong softLimitBytes, ulong hardLimitBytes)
    {
        if (string.IsNullOrWhiteSpace(pool.Value))
        {
            throw new ArgumentException("Supply an initialized memory pool identifier.", nameof(pool));
        }

        if (softLimitBytes > hardLimitBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(softLimitBytes), "The soft limit cannot exceed the hard limit.");
        }

        _budgets[pool] = new(softLimitBytes, hardLimitBytes);
    }
}
