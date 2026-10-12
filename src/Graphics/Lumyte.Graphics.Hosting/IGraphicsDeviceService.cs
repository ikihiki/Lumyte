namespace Lumyte.Graphics.Hosting;

/// <summary>Provides scoped ownership, generation leases and explicit device recovery.</summary>
public interface IGraphicsDeviceService : IAsyncDisposable
{
    /// <summary>Notifies state changes without running recovery automatically.</summary>
    event EventHandler? StateChanged;

    /// <summary>Gets an immutable state snapshot.</summary>
    GraphicsDeviceState State { get; }

    /// <summary>Initializes once; concurrent callers share creation.</summary>
    /// <param name="cancellationToken">Cancels only this caller's wait.</param>
    /// <returns>Initialization completion.</returns>
    ValueTask InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Acquires the current Ready epoch without waiting for recovery.</summary>
    /// <param name="cancellationToken">Cancels before the lease is issued.</param>
    /// <returns>A lease retaining the exact physical device.</returns>
    ValueTask<GraphicsDeviceLease> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>Recreates a Lost or Failed device; concurrent callers share creation.</summary>
    /// <param name="cancellationToken">Cancels only this caller's wait.</param>
    /// <returns>The new state.</returns>
    ValueTask<GraphicsDeviceState> RecoverAsync(CancellationToken cancellationToken = default);

    /// <summary>Confirms safe destruction for a lost epoch even while leases remain.</summary>
    /// <param name="epoch">The retained lost physical generation.</param>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>Retirement confirmation without owner disposal.</returns>
    ValueTask WaitForRetirementAsync(ulong epoch, CancellationToken cancellationToken = default);

    /// <summary>Disposes unused lost owners; failed cleanup remains owned for retry.</summary>
    /// <param name="cancellationToken">Cancels before each cleanup.</param>
    /// <returns>The number of owners released.</returns>
    ValueTask<int> RetireAsync(CancellationToken cancellationToken = default);
}
