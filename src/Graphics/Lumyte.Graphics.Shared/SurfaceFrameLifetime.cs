using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Records local image-lease state and provides an explicit native release wait.</summary>
/// <param name="isNativeReleased">Queries whether native acquisition and presentation have released their synchronization objects.</param>
public sealed class SurfaceFrameLifetime(Func<bool> isNativeReleased)
{
    /// <summary>Gets the local lease state; GPU submissions are managed by the caller.</summary>
    public SurfaceFrameStatus Status { get; private set; } = SurfaceFrameStatus.Acquired;

    /// <summary>Rejects repeated presentation or presentation of a released image.</summary>
    public void ValidatePresent()
    {
        ObjectDisposedException.ThrowIf(Status == SurfaceFrameStatus.Disposed, this);
        if (Status != SurfaceFrameStatus.Acquired)
        {
            throw new InvalidOperationException("Present an acquired image once.");
        }
    }

    /// <summary>Records a native presentation request without querying GPU completion.</summary>
    public void MarkPresented() => Status = SurfaceFrameStatus.Presented;

    /// <summary>Explicitly waits for native acquisition and presentation; the caller separately waits for GPU submissions.</summary>
    /// <param name="cancellationToken">Cancels waiting while retaining the lease.</param>
    /// <returns>Completion of native acquisition and presentation use.</returns>
    public async ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == SurfaceFrameStatus.Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        while (!isNativeReleased())
        {
            await Task.Delay(1, cancellationToken);
        }
    }

    /// <summary>Records native cleanup without checking outstanding GPU use.</summary>
    public void MarkDisposed() => Status = SurfaceFrameStatus.Disposed;
}
