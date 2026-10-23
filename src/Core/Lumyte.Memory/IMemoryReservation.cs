namespace Lumyte.Memory;

/// <summary>Owns an uncommitted portion of a memory budget.</summary>
public interface IMemoryReservation : IDisposable
{
    /// <summary>Gets the original reservation request.</summary>
    MemoryRequest Request { get; }

    /// <summary>Converts the reservation into a committed charge exactly once.</summary>
    /// <param name="usage">The actual or estimated committed usage.</param>
    /// <returns>The owned charge.</returns>
    IMemoryCharge Commit(MemoryUsage usage);
}
