namespace Lumyte.Graphics.Native;

/// <summary>A caller-owned device timeline, shared by CPU operations and queues from the same backend.</summary>
/// <remarks>
/// CPU waits and queries may run concurrently with queue submission and CPU signaling.
/// The caller orders all signal operations so their values strictly increase in execution order,
/// and keeps the semaphore and backend alive until every CPU and GPU signal, wait, and query has ended.
/// Observing a producer signal does not end a consumer queue's use of that semaphore.
/// </remarks>
public abstract class NativeGpuSemaphore : IDisposable
{
    protected NativeGpuSemaphore() { }

    /// <summary>Queries whether the current timeline value is at least the requested value without waiting.</summary>
    public abstract bool IsComplete(ulong value);

    /// <summary>Blocks the calling CPU thread until the requested value is reached; it does not block queue submission.</summary>
    public abstract void WaitCpu(ulong value);

    /// <summary>Asynchronously observes the requested timeline value without blocking the calling thread.</summary>
    /// <remarks>
    /// Cancellation ends only this CPU observation; it does not cancel GPU work or prove GPU use has ended.
    /// Keep the semaphore and backend alive until this operation has completed, failed, or been canceled.
    /// A reached value can also originate from SignalCpu, so this method does not independently prove GPU completion.
    /// The default implementation periodically calls IsComplete without blocking a worker thread.
    /// Backends may override it to use native notifications while preserving these lifetime and failure semantics.
    /// </remarks>
    public virtual ValueTask WaitAsync(ulong value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsComplete(value) ? ValueTask.CompletedTask : ObserveAsync(value, cancellationToken);
    }

    private async ValueTask ObserveAsync(ulong value, CancellationToken cancellationToken)
    {
        do
        {
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        while (!IsComplete(value));
    }

    /// <summary>Signals from the CPU, independently of GPU work; it is not a GPU completion notification.</summary>
    public abstract void SignalCpu(ulong value);

    /// <summary>Destroys the timeline after the caller has resolved all related CPU operations and GPU submissions.</summary>
    public abstract void Dispose();
}
