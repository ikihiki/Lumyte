namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes an explicit binary semaphore wait in a GPU submission.</summary>
public sealed record SemaphoreWaitDesc
{
    /// <summary>Gets the same-device semaphore with a previously issued signal.</summary>
    public required IGraphicsSemaphore Semaphore { get; init; }

    /// <summary>Gets the nonempty GPU stages that wait; Host and unknown stages are invalid.</summary>
    public required PipelineStage Stages { get; init; }
}
