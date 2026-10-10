using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Tracks an image lease without owning a target or inserting GPU operations.</summary>
/// <param name="isNativeReleased">Queries whether native presentation has released its synchronization objects.</param>
public sealed class SurfaceFrameLifetime(Func<bool> isNativeReleased)
{
    private IGraphicsSubmission? _submission;

    /// <summary>Gets the current lease state.</summary>
    public SurfaceFrameStatus Status { get; private set; } = SurfaceFrameStatus.Acquired;

    /// <summary>Gets a value indicating whether all recorded GPU and native image-lease uses have ended without inserting a wait.</summary>
    public bool IsReleased => Status == SurfaceFrameStatus.Disposed || (_submission?.Status != SubmissionStatus.Pending && isNativeReleased());

    /// <summary>Rejects new recording after submission or image release.</summary>
    public void ValidateRecording()
    {
        ObjectDisposedException.ThrowIf(Status == SurfaceFrameStatus.Disposed, this);
        if (Status != SurfaceFrameStatus.Acquired)
        {
            throw new InvalidOperationException("Record acquired images before their sole submission.");
        }
    }

    /// <summary>Associates the explicitly issued submission after successful queue submission.</summary>
    /// <param name="submission">The GPU completion handle.</param>
    public void MarkSubmitted(IGraphicsSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ValidateRecording();
        _submission = submission;
        Status = SurfaceFrameStatus.Submitted;
    }

    /// <summary>Rejects presentation before submission or repeated presentation.</summary>
    public void ValidatePresent()
    {
        ObjectDisposedException.ThrowIf(Status == SurfaceFrameStatus.Disposed, this);
        if (Status != SurfaceFrameStatus.Submitted)
        {
            throw new InvalidOperationException("Present a submitted frame exactly once.");
        }
    }

    /// <summary>Records a native presentation request without waiting for GPU completion.</summary>
    public void MarkPresented()
    {
        ValidatePresent();
        Status = SurfaceFrameStatus.Presented;
    }

    /// <summary>Rejects disposal until both GPU and native presentation use have ended.</summary>
    public void ValidateRelease()
    {
        if (Status == SurfaceFrameStatus.Disposed)
        {
            return;
        }

        if (!IsReleased)
        {
            throw new InvalidOperationException("Wait for GPU and presentation use before releasing the frame.");
        }
    }

    /// <summary>Explicitly waits for lease release without submitting or presenting.</summary>
    /// <param name="cancellationToken">Cancels waiting while retaining the lease.</param>
    /// <returns>Completion of native resource use.</returns>
    public async ValueTask WaitForReleaseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == SurfaceFrameStatus.Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        while (!IsReleased)
        {
            await Task.Delay(1, cancellationToken);
        }
    }

    /// <summary>Records successful native cleanup.</summary>
    public void MarkDisposed() => Status = SurfaceFrameStatus.Disposed;
}
