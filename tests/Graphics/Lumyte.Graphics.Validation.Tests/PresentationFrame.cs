using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class PresentationFrame : IGraphicsSurfaceFrame
{
    public PresentationTexture Image { get; } = new();

    public bool ThrowOnTexture { get; set; }

    public int DisposeCount { get; private set; }

    public int PresentCount { get; private set; }

    public int ReleaseWaitCount { get; private set; }

    public SurfaceFrameStatus Status { get; private set; } = SurfaceFrameStatus.Acquired;

    public IGraphicsTexture Texture => ThrowOnTexture ? throw new InvalidOperationException("Native texture lookup failed.") : Image;

    public SurfaceStatus Present(IReadOnlyList<IGraphicsSemaphore>? waitSemaphores = null)
    {
        PresentCount++;
        return SurfaceStatus.Success;
    }

    public ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReleaseWaitCount++;
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        DisposeCount++;
        Status = SurfaceFrameStatus.Disposed;
    }
}
