namespace Lumyte.Graphics.Native.Resources;

public sealed class GpuResourceBatch : IDisposable
{
    internal GpuResourceBatch(GpuResourceManager manager, NativeGpuQueue queue) { Manager = manager; Queue = queue; }
    internal GpuResourceManager Manager { get; }
    internal NativeGpuQueue Queue { get; }
    private readonly HashSet<ResourceRecord> uses = [];
    private readonly List<GpuResourceScope> scopes = [];
    private readonly List<IDisposable> leases = [];
    internal readonly List<NativeGpuCommandBuffer> Commands = [];
    private bool started;
    private bool recordingFailed;
    private bool submitted;
    private bool disposed;
    private bool released;
    private bool releaseAttempted;
    private Exception? releaseFailure;
    internal bool EndRequested => disposed;
    internal void RequireRecording()
    {
        Manager.RequireOpen();
        if (disposed || submitted || recordingFailed) { throw new InvalidOperationException("The batch is closed, failed during recording setup, or already submitted."); }
    }
    private void RequireOwnershipChange()
    { RequireRecording(); if (started) { throw new InvalidOperationException("Declare resource uses before starting command recording."); } }
    public void Use(GpuResourceRef reference)
    {
        RequireOwnershipChange(); ResourceRecord record = Manager.Require(reference);
        if (uses.Add(record)) { record.Holds++; }
    }
    public void Use(GpuResourceScope scope)
    {
        RequireOwnershipChange(); scope.RequireOpen();
        if (!ReferenceEquals(scope.Manager, Manager)) { throw new ArgumentException("The scope belongs to another manager.", nameof(scope)); }
        uses.EnsureCapacity(checked(uses.Count + scope.Records.Count));
        foreach (ResourceRecord record in scope.Records) { if (uses.Add(record)) { record.Holds++; } }
    }
    public void Own(GpuResourceScope scope)
    {
        RequireOwnershipChange(); scope.RequireOpen();
        if (!ReferenceEquals(scope.Manager, Manager)) { throw new ArgumentException("The scope belongs to another manager.", nameof(scope)); }
        scopes.Add(scope); scope.Owned = true;
    }
    public void Retain(IDisposable lease)
    {
        RequireOwnershipChange(); ArgumentNullException.ThrowIfNull(lease);
        if (leases.Any(existing => ReferenceEquals(existing, lease))) { throw new ArgumentException("The batch already owns this lease.", nameof(lease)); }
        leases.EnsureCapacity(checked(leases.Count + 1));
        uses.EnsureCapacity(checked(uses.Count + 1));
        ResourceRecord? record = lease switch
        {
            GpuResourceUse use => use.TransferToBatch(Manager),
            GpuResourcePin pin => pin.TransferToBatch(Manager),
            _ => null,
        };
        // Keep a separate batch hold until every retained dependency has ended.
        if (record is not null && uses.Add(record)) { record.Holds++; }
        leases.Add(lease);
    }
    public NativeGpuCommandBuffer StartCommandRecording()
    {
        RequireRecording();
        Commands.EnsureCapacity(checked(Commands.Count + 1));
        NativeGpuCommandBuffer commands = Queue.StartCommandRecording();
        Commands.Add(commands);
        started = true;
        try
        {
            if (ReferenceEquals(Queue, Manager.Backend.MainQueue))
            {
                commands.SetResourceDescriptorHeap(Manager.ResourceDescriptorHeap);
                commands.SetSamplerDescriptorHeap(Manager.SamplerDescriptorHeap);
            }
            return commands;
        }
        catch
        {
            // The batch already owns the recording and all declared dependencies. Disposal
            // remains ordered with the other recordings, even if initialization failed.
            recordingFailed = true;
            throw;
        }
    }
    public GpuSubmissionToken Submit()
    { RequireRecording(); submitted = true; return Manager.Submit(this); }
    internal void Reject()
    {
        submitted = false;
        disposed = true;
        ReleaseOwned();
        Manager.Batches.Remove(this);
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        if (!submitted) { ReleaseOwned(); }
        Manager.Batches.Remove(this);
    }
    internal void ReleaseOwned()
    {
        if (released) { return; }
        if (releaseAttempted) { throw releaseFailure!; }
        // Destroy recordings before returning references they borrowed. A failed destruction
        // leaves the resource holds intact; the manager will not retry an ambiguous release.
        Exception[] errors = new Exception[checked(Commands.Count + leases.Count)];
        int errorCount = 0;
        releaseAttempted = true;
        foreach (NativeGpuCommandBuffer commands in Commands)
        { try { commands.Dispose(); } catch (Exception error) { errors[errorCount++] = error; } }
        if (errorCount == 0)
        {
            foreach (IDisposable lease in leases)
            {
                try
                {
                    if (lease is GpuResourceUse use) { use.ReturnFromBatch(); }
                    else if (lease is GpuResourcePin pin) { pin.ReturnFromBatch(); }
                    else { lease.Dispose(); }
                }
                catch (Exception error) { errors[errorCount++] = error; }
            }
        }
        if (errorCount != 0)
        {
            releaseFailure = new AggregateException("Command or retained lease cleanup failed; resource holds remain owned.", errors.Take(errorCount));
            throw releaseFailure;
        }
        Commands.Clear(); leases.Clear();
        released = true;
        foreach (ResourceRecord record in uses) { record.Holds--; }
        uses.Clear();
        foreach (GpuResourceScope scope in scopes) { scope.Close(); }
        scopes.Clear();
    }
}

public sealed partial class GpuResourceManager
{
    internal GpuSubmissionToken Submit(GpuResourceBatch batch)
    {
        QueueTimeline timeline;
        Submission submission;
        NativeGpuCommandBuffer[] commands;
        ulong value;
        try
        {
            if (!timelines.TryGetValue(batch.Queue, out timeline!))
            {
                NativeGpuSemaphore semaphore = Backend.CreateSemaphore();
                try { timelines.Add(batch.Queue, timeline = new(semaphore)); }
                catch { semaphore.Dispose(); throw; }
            }
            value = checked(timeline.NextValue + 1);
            commands = batch.Commands.ToArray();
            submission = new(this, batch);
            submissions.EnsureCapacity(checked(submissions.Count + 1));
        }
        catch (Exception error)
        {
            try { batch.Reject(); }
            catch (Exception cleanup) { throw new AggregateException("Submission preparation and cleanup failed.", error, cleanup); }
            throw;
        }
        GpuSubmissionToken token = new(submission);
        submissions.Add(submission);
        timeline.NextValue = value;
        try { batch.Queue.Submit(commands, new(timeline.Semaphore, value)); }
        catch (Exception error)
        {
            Exception cause = PublicCause(error);
            submission.Fail(error, cause);
            _ = submission.ObserveAsync(timeline.Semaphore, value);
            throw new GpuSubmissionException(token, cause);
        }
        _ = submission.ObserveAsync(timeline.Semaphore, value);
        return token;
    }
    internal static Exception PublicCause(Exception error)
    {
        if (error is NativeGpuSubmissionException submission) { return PublicCause(submission.InnerException!); }
        if (error is AggregateException aggregate)
        {
            Exception[] causes = aggregate.InnerExceptions.Select(PublicCause).ToArray();
            if (causes.Where((cause, index) => !ReferenceEquals(cause, aggregate.InnerExceptions[index])).Any())
            { return new AggregateException(aggregate.Message, causes); }
            return error;
        }
        if (error.InnerException is { } inner)
        {
            Exception cause = PublicCause(inner);
            if (!ReferenceEquals(inner, cause))
            { return new InvalidOperationException($"{error.GetType().Name}: {error.Message}", cause); }
        }
        return error;
    }
}
