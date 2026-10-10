namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one presentation image lease; caller-owned semaphores are supplied explicitly.</summary>
public interface IGraphicsSurfaceFrame : IDisposable
{
    /// <summary>Gets the local acquisition and presentation state; GPU submissions are not tracked.</summary>
    SurfaceFrameStatus Status { get; }

    /// <summary>Gets the borrowed image; do not dispose it directly.</summary>
    IGraphicsTexture Texture { get; }

    /// <summary>Requests presentation after the frame's explicit queue submission without a CPU completion wait.</summary>
    /// <param name="waitSemaphores">The caller-selected same-device binary waits; omitted waits are not inserted automatically.</param>
    /// <returns>The native presentation outcome; no automatic resize or recovery occurs.</returns>
    SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null);

    /// <summary>Explicitly waits for native acquisition and presentation use; wait for GPU submissions separately before disposal.</summary>
    /// <param name="cancellationToken">Cancels waiting without releasing the frame.</param>
    /// <returns>The completion of native acquisition and presentation, not a display timestamp or a submission wait.</returns>
    ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default);
}
