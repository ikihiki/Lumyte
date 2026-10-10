namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes exactly the commands, GPU waits and GPU signals supplied by the caller.</summary>
public sealed record QueueSubmitDesc
{
    /// <summary>Gets executable commands in submission order; an empty list requires at least one wait or signal.</summary>
    public IReadOnlyList<IGraphicsCommandBuffer> CommandBuffers { get; init; } = [];

    /// <summary>Gets explicit consuming waits; no acquisition wait is added automatically.</summary>
    public IReadOnlyList<SemaphoreWaitDesc> WaitSemaphores { get; init; } = [];

    /// <summary>Gets binary semaphores signaled after all submitted commands; no presentation signal is added automatically.</summary>
    public IReadOnlyList<IGraphicsSemaphore> SignalSemaphores { get; init; } = [];
}
