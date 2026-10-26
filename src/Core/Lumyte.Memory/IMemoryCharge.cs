namespace Lumyte.Memory;

/// <summary>Tracks a committed external allocation without freeing it.</summary>
public interface IMemoryCharge : IDisposable
{
    /// <summary>Gets the charged pool.</summary>
    MemoryPoolId Pool { get; }

    /// <summary>Gets the committed usage.</summary>
    MemoryUsage Usage { get; }
}
