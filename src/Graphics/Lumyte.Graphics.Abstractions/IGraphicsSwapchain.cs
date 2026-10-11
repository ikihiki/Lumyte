namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns a configured presentation image set; the caller explicitly reconfigures it.</summary>
public interface IGraphicsSwapchain : IDisposable
{
    /// <summary>Gets the requested and effective configuration without implicit size correction.</summary>
    SwapchainDesc Configuration { get; }

    /// <summary>Reconfigures only after every acquired frame has been disposed.</summary>
    /// <param name="desc">The exact new configuration.</param>
    void Reconfigure(SwapchainDesc desc);

    /// <summary>Attempts acquisition without waiting for an unavailable image.</summary>
    /// <param name="signalSemaphore">An optional same-device binary semaphore signaled by successful acquisition; null requests no semaphore signal.</param>
    /// <param name="cancellationToken">Cancels before native acquisition; a successful acquisition always returns its owned lease.</param>
    /// <returns>A usable frame or a recovery status without waiting for GPU image readiness on the CPU.</returns>
    ValueTask<SurfaceAcquireResult> AcquireNextFrameAsync(IGraphicsSemaphore? signalSemaphore = null, CancellationToken cancellationToken = default);
}
