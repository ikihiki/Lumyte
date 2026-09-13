namespace Lumyte.Graphics.Native.Resources;

public readonly struct GpuSubmissionToken : IEquatable<GpuSubmissionToken>
{
    private readonly Submission? submission;
    internal GpuSubmissionToken(Submission submission) => this.submission = submission;
    internal Submission RequireSubmission() => submission ?? throw new InvalidOperationException("The submission token is invalid.");
    public bool IsValid => submission is not null;
    public bool IsComplete => submission?.Ended.IsCompletedSuccessfully == true;
    public Task WaitAsync(CancellationToken cancellationToken = default) => RequireSubmission().Succeeded.WaitAsync(cancellationToken);
    public bool Equals(GpuSubmissionToken other) => ReferenceEquals(submission, other.submission);
    public override bool Equals(object? obj) => obj is GpuSubmissionToken other && Equals(other);
    public override int GetHashCode() => submission?.GetHashCode() ?? 0;
    public static bool operator ==(GpuSubmissionToken left, GpuSubmissionToken right) => left.Equals(right);
    public static bool operator !=(GpuSubmissionToken left, GpuSubmissionToken right) => !left.Equals(right);
}

public sealed class GpuSubmissionException : Exception
{
    internal GpuSubmissionException(GpuSubmissionToken completion, Exception innerException)
        : base("GPU submission failed after handoff occurred or may have occurred.", innerException) => Completion = completion;
    public GpuSubmissionToken Completion { get; }
}

internal sealed class QueueTimeline(NativeGpuSemaphore semaphore)
{
    internal NativeGpuSemaphore Semaphore { get; } = semaphore;
    internal ulong NextValue;
}

internal sealed class Submission(GpuResourceManager owner, GpuResourceBatch batch)
{
    internal GpuResourceManager Owner { get; } = owner;
    internal NativeGpuQueue Queue => batch.Queue;
    internal bool Accepted;
    private readonly List<IDisposable> retained = [];
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource succeeded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Ended => ended.Task;
    internal Task Succeeded => succeeded.Task;
    internal readonly List<ResourceRecord> Retired = [];
    internal bool ReleaseAttempted;
    internal Exception? ReleaseFailure;
    internal bool CanRelease => batch.EndRequested && Ended.IsCompletedSuccessfully;
    internal void Retain(IDisposable lease)
    {
        if (ReleaseFailure is not null) { throw ReleaseFailure; }
        if (retained.Any(existing => ReferenceEquals(existing, lease)))
        { throw new ArgumentException("The submission already owns this lease.", nameof(lease)); }
        retained.EnsureCapacity(checked(retained.Count + 1));
        if (lease is GpuResourceUse use) { use.TransferToBatch(Owner); }
        else if (lease is GpuResourcePin pin) { pin.TransferToBatch(Owner); }
        if (ReleaseAttempted)
        {
            retained.Add(lease);
            try { ReturnLease(lease); retained.Remove(lease); }
            catch (Exception error) { ReleaseFailure = error; throw; }
            return;
        }
        retained.Add(lease);
    }
    private Exception? rawFailure;
    internal void Fail(Exception raw, Exception publicCause)
    { rawFailure = raw; succeeded.TrySetException(publicCause); _ = succeeded.Task.Exception; }
    internal async Task ObserveAsync(NativeGpuSemaphore semaphore, ulong value)
    {
        try { await semaphore.WaitAsync(value); ended.TrySetResult(); succeeded.TrySetResult(); }
        catch (Exception error)
        {
            Exception cause = GpuResourceManager.PublicCause(error);
            ended.TrySetException(cause); _ = ended.Task.Exception;
            succeeded.TrySetException(cause); _ = succeeded.Task.Exception;
        }
    }
    internal void Release()
    {
        if (ReleaseAttempted) { return; }
        ReleaseAttempted = true;
        try
        {
            batch.ReleaseOwned();
            foreach (IDisposable lease in retained) { ReturnLease(lease); }
            retained.Clear();
            foreach (ResourceRecord record in Retired) { record.Holds--; }
            Retired.Clear();
        }
        catch (Exception error) { ReleaseFailure = error; throw; }
        GC.KeepAlive(rawFailure);
    }
    private static void ReturnLease(IDisposable lease)
    {
        if (lease is GpuResourceUse use) { use.ReturnFromBatch(); }
        else if (lease is GpuResourcePin pin) { pin.ReturnFromBatch(); }
        else { lease.Dispose(); }
    }
}
