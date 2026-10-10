namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides the device-owned general queue.</summary>
public interface IGraphicsQueue
{
    /// <summary>Submits executable command buffers in order without waiting.</summary>
    /// <param name="commandBuffers">The commandBuffers value.</param>
    /// <returns>The owned completion handle.</returns>
    IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers);

    /// <summary>Submits exactly the caller's commands, consuming waits and completion signals without a CPU wait.</summary>
    /// <param name="desc">The explicit submission; no surface frame or automatically inserted synchronization.</param>
    /// <returns>The owned GPU completion handle.</returns>
    IGraphicsSubmission Submit(QueueSubmitDesc desc);
}
