namespace Lumyte.Memory;

/// <summary>Owns memory reservations and charges for one runtime lifetime.</summary>
public interface IMemoryScope : IAsyncDisposable
{
    /// <summary>Gets the diagnostic owner name.</summary>
    string Owner { get; }

    /// <summary>Reserves bytes without invoking reclamation.</summary>
    /// <param name="request">The reservation request.</param>
    /// <param name="cancellationToken">Cancels the operation before reservation.</param>
    /// <returns>The owned reservation.</returns>
    ValueTask<IMemoryReservation> ReserveAsync(MemoryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Rents and charges a typed CPU buffer.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="pool">The charged pool.</param>
    /// <param name="length">The requested element length.</param>
    /// <param name="purpose">The retention purpose.</param>
    /// <param name="cancellationToken">Cancels the operation before allocation.</param>
    /// <returns>The owned buffer.</returns>
    ValueTask<IMemoryBuffer<T>> RentAsync<T>(MemoryPoolId pool, int length, MemoryPurpose purpose, CancellationToken cancellationToken = default)
        where T : unmanaged;
}
