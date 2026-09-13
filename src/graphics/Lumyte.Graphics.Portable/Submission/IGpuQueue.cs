namespace Lumyte.Graphics.Portable;

/// <summary>A backend-owned Portable queue with explicit submission and CPU observation of completion.</summary>
/// <remarks>
/// The caller serializes submissions on this queue. Completion queries and asynchronous waits can run
/// concurrently with submission. Timelines do not expose GPU waits or CPU signals.
/// </remarks>
public interface IGpuQueue
{
    GpuCommandBuffer StartCommandRecording();

    /// <summary>Consumes a nonempty span of one-shot recordings from this queue and signals the specified timeline value.</summary>
    /// <remarks>
    /// Input span storage is consumed before return. Native pipeline creation and encoding occur here;
    /// GPU completion and asynchronous diagnostics are observed separately. Signal values strictly increase.
    /// A handled failure during or after runtime handoff is reported as GpuSubmissionException with
    /// the affected point. Handoff may have occurred without acceptance or completion being confirmed.
    /// This exception contract does not classify arbitrary catastrophic host failures.
    /// </remarks>
    void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue);

    GpuSemaphore CreateSemaphore(ulong initialValue = 0);

    /// <summary>Reports GPU use ending at an initial value or a point whose runtime handoff began; true does not establish processing success.</summary>
    /// <remarks>A handoff failure can leave completion unknown; observation then reports the connection failure.</remarks>
    bool IsComplete(GpuSemaphore semaphore, ulong value);

    /// <summary>Waits for both GPU use to end and this submission's diagnostics to establish success.</summary>
    /// <remarks>Cancellation affects only this wait. It does not cancel GPU work or establish safe resource reclamation.</remarks>
    ValueTask WaitAsync(GpuSemaphore semaphore, ulong value, CancellationToken cancellationToken = default);
}
