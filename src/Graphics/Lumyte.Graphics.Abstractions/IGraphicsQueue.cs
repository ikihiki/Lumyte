namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides the device-owned general queue.</summary>
public interface IGraphicsQueue
{
    /// <summary>Submits executable command buffers in order without waiting.</summary>
    /// <param name="commandBuffers">The commandBuffers value.</param>
    /// <returns>The owned completion handle.</returns>
    IGraphicsSubmission Submit(IReadOnlyList<IGraphicsCommandBuffer> commandBuffers);
}
