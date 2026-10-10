namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns one presentation image lease and its native synchronization objects.</summary>
public interface IGraphicsSurfaceFrame : IDisposable
{
    /// <summary>Gets the acquisition, submission and presentation lifetime state.</summary>
    SurfaceFrameStatus Status { get; }

    /// <summary>Gets the borrowed image; do not dispose it directly.</summary>
    IGraphicsTexture Texture { get; }

    /// <summary>Requests presentation after the frame's explicit queue submission without a CPU completion wait.</summary>
    /// <returns>The native presentation outcome; no automatic resize or recovery occurs.</returns>
    SurfaceStatus Present();

    /// <summary>Explicitly waits until GPU and presentation use allow frame disposal.</summary>
    /// <param name="cancellationToken">Cancels waiting without releasing the frame.</param>
    /// <returns>The completion of image-lease use, not a display timestamp.</returns>
    ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default);
}
