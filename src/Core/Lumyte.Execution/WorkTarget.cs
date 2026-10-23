namespace Lumyte.Execution;

/// <summary>A CPU or fixed-owner execution target.</summary>
public readonly struct WorkTarget
{
    private readonly bool _cpu;

    private WorkTarget(OwnerDefinition? owner, bool cpu)
    {
        Owner = owner;
        _cpu = cpu;
    }

    /// <summary>Gets the CPU target.</summary>
    public static WorkTarget Cpu => new(null, true);

    internal OwnerDefinition? Owner { get; }

    internal bool IsValid => _cpu || Owner is not null;

    /// <summary>Targets an explicitly declared owner.</summary>
    /// <param name="owner">The owner.</param>
    /// <returns>The owner target.</returns>
    public static WorkTarget On(OwnerKey owner) => new(owner.Definition ?? throw new ArgumentException("Invalid owner.", nameof(owner)), false);
}
