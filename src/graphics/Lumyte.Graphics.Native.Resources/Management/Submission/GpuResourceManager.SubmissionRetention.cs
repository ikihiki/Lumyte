namespace Lumyte.Graphics.Native.Resources;

public sealed partial class GpuResourceManager
{
    /// <summary>Identifies an accepted main-queue submission issued by this manager.</summary>
    public bool OwnsSubmission(GpuSubmissionToken token)
        => token.IsValid && ReferenceEquals(token.RequireSubmission().Owner, this)
            && ReferenceEquals(token.RequireSubmission().Queue, Backend.MainQueue) && token.RequireSubmission().Accepted;

    /// <summary>Transfers a lease to a main-queue submission from this manager, including an uncertain submission.</summary>
    /// <remarks>On normal return the manager owns the lease until GPU use ends. The caller serializes this operation with collection.</remarks>
    public void RetainUntilSubmissionEnds(GpuSubmissionToken token, IDisposable lease)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(lease);
        Submission submission = Require(token);
        if (!ReferenceEquals(submission.Queue, Backend.MainQueue))
        { throw new ArgumentException("The token must identify a main-queue submission from this manager.", nameof(token)); }
        try { submission.Retain(lease); }
        catch (Exception error)
        {
            if (submission.ReleaseFailure is not null && !failures.Contains(error)) { failures.Add(error); }
            throw;
        }
    }
}
