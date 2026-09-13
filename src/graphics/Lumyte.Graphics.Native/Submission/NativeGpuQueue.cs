namespace Lumyte.Graphics.Native;

/// <summary>A backend-owned native queue with explicit GPU dependencies and caller-owned device timelines.</summary>
/// <remarks>
/// The caller serializes operations on this queue; distinct queues may be used concurrently.
/// Semaphore CPU waits and queries do not access queue state and may run concurrently with submission.
/// Signal values increase monotonically in execution order, including signals from other queues or the CPU.
/// Submit accepts the whole batch once and does not wait for GPU completion.
/// Recordings whose preparation fails must be disposed; the caller may record a replacement batch.
/// Submitted or possibly submitted recordings cannot be reused or automatically resubmitted.
/// </remarks>
public abstract class NativeGpuQueue
{
    protected NativeGpuQueue() { }

    public abstract NativeGpuCommandBuffer StartCommandRecording();

    /// <summary>Submits one-shot recordings after the GPU waits for every supplied point, then signals completion.</summary>
    /// <remarks>
    /// Waits cover the whole batch. An empty command span submits only the dependencies and signal.
    /// Span storage is consumed before return; all referenced semaphores remain alive through their GPU uses.
    /// A wait may precede its signal submission when forward progress and resource initialization are guaranteed.
    /// GPU waits do not perform texture layout transitions or manage application-resource lifetimes.
    /// A synchronous failure after handoff began is reported as NativeGpuSubmissionException unless
    /// the native API guarantees that the batch was not submitted. Its Completion is the requested signal,
    /// not a guarantee of arrival or GPU use ending. Keep resources and semaphores alive until their use ends.
    /// An unclassified host exception does not by itself prove that no handoff occurred.
    /// </remarks>
    /// <exception cref="NativeGpuSubmissionException">GPU handoff occurred or may have occurred before failure.</exception>
    public abstract void Submit(ReadOnlySpan<NativeGpuCommandBuffer> commands,
        NativeGpuTimelinePoint signal, ReadOnlySpan<NativeGpuTimelinePoint> waits = default);
}
