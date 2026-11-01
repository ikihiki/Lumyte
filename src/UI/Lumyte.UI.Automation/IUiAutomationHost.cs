namespace Lumyte.UI.Automation;

/// <summary>A borrowed surface host responsible for sequential UI dispatch and frame progress.</summary>
public interface IUiAutomationHost
{
    /// <summary>Gets the monotonic clock used for all operation deadlines.</summary>
    TimeProvider TimeProvider { get; }

    /// <summary>Executes a synchronous operation once on the UI host, without retaining the surface.</summary>
    /// <typeparam name="T">The detached result.</typeparam>
    /// <param name="action">The operation; it must not retain the borrowed surface.</param>
    /// <param name="cancellationToken">Cancels before invocation, never interrupts a synchronous callback.</param>
    /// <returns>The operation result.</returns>
    ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default);

    /// <summary>Advances to a new layout/update frame after invocation; GPU completion is separate.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The frame boundary.</returns>
    ValueTask NextFrameAsync(CancellationToken cancellationToken = default);

    /// <summary>Reserves neutral input exclusively, deferring physical input and reserving a non-conflicting pointer identity.</summary>
    /// <param name="cancellationToken">Cancels reservation.</param>
    /// <returns>An input lease whose uncancellable cleanup precedes returning exclusivity.</returns>
    ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default);
}
