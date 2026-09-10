namespace Lumyte.Graphics.Native;

/// <summary>A backend-owned native queue with caller-owned completion timelines.</summary>
/// <remarks>
/// The caller serializes queue operations and access to each associated semaphore.
/// Signal values increase monotonically. Submit accepts the whole batch once and does not wait for GPU completion.
/// A failed batch must be disposed and recorded again; submitted recordings cannot be reused.
/// </remarks>
public abstract class NativeGpuQueue
{
    protected NativeGpuQueue() { }

    public abstract NativeGpuCommandBuffer StartCommandRecording();

    public abstract void Submit(
        ReadOnlySpan<NativeGpuCommandBuffer> commands, NativeGpuSemaphore semaphore, ulong value);

    public abstract NativeGpuSemaphore CreateSemaphore(ulong initialValue);

    public abstract bool IsComplete(NativeGpuSemaphore semaphore, ulong value);

    /// <summary>Explicitly waits for the requested completion value on the CPU.</summary>
    public abstract void Wait(NativeGpuSemaphore semaphore, ulong value);
}
