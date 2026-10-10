namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides the device-owned general queue.</summary>
public interface IGraphicsQueue
{
    /// <summary>Submits executable command buffers in order without waiting.</summary>
    /// <param name="commandBuffers">The commandBuffers value.</param>
    /// <returns>The owned completion handle.</returns>
    IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers);

    /// <summary>Submits one acquired frame's commands, associating native presentation synchronization.</summary>
    /// <param name="commandBuffers">The executable commands, ending in Present state for the acquired image.</param>
    /// <param name="frame">The same-device, never-submitted frame used by these commands.</param>
    /// <returns>The owned GPU completion handle; presentation remains an explicit frame operation.</returns>
    IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers, IGraphicsSurfaceFrame frame);
}
