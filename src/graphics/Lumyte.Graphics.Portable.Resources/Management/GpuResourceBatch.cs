namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Owns recordings and explicit resource uses through submission and confirmed GPU completion.</summary>
public sealed class GpuResourceBatch : IDisposable
{
    private readonly GpuResourceManager manager;
    private readonly List<ResourceRecord> holds = [];
    private readonly HashSet<ResourceRecord> uses = [];
    private readonly List<RetainedLease> retained = [];
    private readonly List<GpuCommandBuffer> commands = [];
    private bool sealedBatch;
    internal bool EndRequested { get; private set; }
    internal bool Cleaned { get; private set; }
    internal Exception? CleanupError { get; private set; }
    internal ManagedSubmission? Submission { get; private set; }
    internal GpuResourceBatch(GpuResourceManager manager) { this.manager = manager; }
    private void CheckOpen()
    {
        manager.CheckOpen();
        if (sealedBatch || EndRequested) { throw new InvalidOperationException("The batch no longer accepts recordings or dependencies."); }
    }
    public void Use(GpuResourceRef resource)
    {
        CheckOpen();
        ResourceRecord record = manager.Check(resource);
        if (uses.Contains(record)) { return; }
        holds.EnsureCapacity(holds.Count + 1);
        uses.Add(record); holds.Add(record); record.Holds++;
    }
    public void Use(GpuResourceScope scope)
    {
        CheckScope(scope);
        foreach (ResourceRecord record in scope.Snapshot()) { Use(record.Reference); }
    }
    public void Own(GpuResourceScope scope)
    {
        CheckScope(scope);
        ResourceRecord[] records = scope.Snapshot();
        holds.EnsureCapacity(holds.Count + records.Length);
        scope.Transfer();
        holds.AddRange(records);
    }
    private void CheckScope(GpuResourceScope scope)
    {
        CheckOpen(); ArgumentNullException.ThrowIfNull(scope);
        if (!ReferenceEquals(scope.Manager, manager)) { throw new ArgumentException("The scope belongs to another manager.", nameof(scope)); }
    }
    public void Retain(IDisposable lease)
    {
        CheckOpen(); ArgumentNullException.ThrowIfNull(lease);
        if (retained.Any(item => ReferenceEquals(item.Original, lease)))
        { throw new InvalidOperationException("The lease is already retained by this batch."); }
        IManagedBatchLease? managed = lease as IManagedBatchLease;
        ResourceRecord? record = managed?.PrepareTransfer(manager);
        retained.EnsureCapacity(retained.Count + 1);
        if (record is not null)
        {
            holds.EnsureCapacity(holds.Count + 1);
            managed!.Transfer(); record.Holds++; holds.Add(record);
        }
        retained.Add(new(lease, managed));
    }
    public GpuCommandBuffer StartCommandRecording()
    {
        CheckOpen(); commands.EnsureCapacity(commands.Count + 1);
        GpuCommandBuffer recording = manager.Queue.StartCommandRecording();
        commands.Add(recording); return recording;
    }
    public GpuSubmissionToken Submit()
    {
        CheckOpen();
        if (commands.Count == 0) { throw new InvalidOperationException("A Portable batch requires at least one recording."); }
        GpuCommandBuffer[] recordings = commands.ToArray();
        ManagedSubmission submission = manager.PrepareSubmission();
        Submission = submission; sealedBatch = true;
        var token = new GpuSubmissionToken(submission);
        try { manager.Queue.Submit(recordings, submission.Point.Semaphore, submission.Point.Value); }
        catch (Exception error)
        {
            Exception cause = ManagedSubmission.Sanitize(error);
            submission.Outcome.TrySetException(cause);
            submission.OutcomeObserved = true;
            _ = submission.Outcome.Task.Exception;
            // Even when the call throws, a registered point may later prove that GPU use ended.
            // No ordinary exception is treated as evidence of rejection of arbitrary host failures.
            throw new GpuSubmissionException(token, cause);
        }
        _ = submission.ObserveAsync();
        return token;
    }
    public void Dispose()
    {
        if (EndRequested) { return; }
        EndRequested = true; sealedBatch = true; manager.CloseOwner();
        if (Submission is null) { Cleanup(); }
    }
    internal void Cleanup()
    {
        if (Cleaned) { return; }
        if (CleanupError is not null) { throw CleanupError; }
        var errors = new Exception[commands.Count + retained.Count];
        int errorCount = 0;
        // Prepare error storage before detaching any external lifetime. Every independent target
        // is attempted once, even when another target fails. An uncertain target keeps all holds.
        foreach (GpuCommandBuffer recording in commands)
        {
            try { recording.Dispose(); } catch (Exception error) { errors[errorCount++] = error; }
        }
        // Retained leases can own memory referenced by the recordings; they are not independent
        // cleanup targets until every recording has ended. In particular a managed mapping can
        // supply the only hold on its buffer.
        if (errorCount == 0)
        {
            foreach (RetainedLease lease in retained)
            {
                try { lease.Dispose(); } catch (Exception error) { errors[errorCount++] = error; }
            }
        }
        if (errorCount != 0)
        {
            CleanupError = errorCount == 1 ? errors[0] : new AggregateException(errors.Take(errorCount));
            throw CleanupError;
        }
        foreach (ResourceRecord record in holds) { record.Holds--; }
        holds.Clear(); uses.Clear(); commands.Clear(); retained.Clear(); Cleaned = true;
    }
    private readonly record struct RetainedLease(IDisposable Original, IManagedBatchLease? Managed)
    {
        internal void Dispose()
        { if (Managed is null) { Original.Dispose(); } else { Managed.ReleaseFromBatch(); } }
    }
}
