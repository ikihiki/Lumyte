using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Portable manager options. GPU resource descriptions are never rewritten by the manager.</summary>
public sealed record GpuResourceManagerOptions;

/// <summary>Logical live ownership counts; buffer bytes exclude idle pool storage and are not physical GPU allocation sizes.</summary>
public readonly record struct GpuResourceStatistics(int ResourceCount, int BufferCount, int TextureCount,
    int ViewCount, int SamplerCount, int BindingsCount, int PackageCount, ulong LogicalBufferBytes,
    int PendingSubmissionCount, int QuarantinedResourceCount);

/// <summary>Explicit ownership, cached bindings and whole-resource reuse on a borrowed Portable backend.</summary>
/// <remarks>Serialize mutations in the backend execution context. Collection performs no background GPU destruction.</remarks>
public sealed partial class GpuResourceManager : IAsyncDisposable
{
    internal IPortableGpuBackend Backend { get; }
    internal IGpuQueue Queue => Backend.MainQueue;
    private readonly GpuBufferPool buffers;
    private readonly GpuTexturePool textures;
    private readonly GpuSemaphore semaphore;
    private readonly List<ResourceRecord> records = [];
    private readonly List<GpuResourceBatch> batches = [];
    private readonly List<ManagedSubmission> submissions = [];
    private readonly List<(ResourceRecord Record, ManagedSubmission Submission)> retirements = [];
    private readonly Dictionary<(ResourceRecord, GpuTextureViewDescription), GpuViewRef> views = [];
    private readonly Dictionary<GpuSamplerDescription, GpuSamplerRef> samplers = [];
    private readonly Dictionary<BindingCacheKey, GpuBindingsRef> bindings = [];
    private int owners;
    private ulong nextSignal;
    private bool ending;
    private bool disposed;
    public GpuResourceManager(IPortableGpuBackend backend, GpuResourceManagerOptions? options = null)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
        buffers = new(backend); textures = new(backend); semaphore = backend.MainQueue.CreateSemaphore();
    }
    internal void CheckOpen() { ObjectDisposedException.ThrowIf(disposed || ending, this); }
    internal ResourceRecord Check(GpuResourceRef resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Record.Manager, this)) { throw new ArgumentException("The resource belongs to another manager.", nameof(resource)); }
        if (!resource.Record.Alive) { throw new ObjectDisposedException(resource.GetType().Name, "The resource identity has been reclaimed or quarantined."); }
        return resource.Record;
    }
    internal void CloseOwner() => owners--;
    public GpuResourceScope CreateScope() { CheckOpen(); var scope = new GpuResourceScope(this); owners++; return scope; }
    public GpuResourcePin Pin(GpuResourceRef resource) { CheckOpen(); var pin = new GpuResourcePin(Check(resource)); owners++; return pin; }
    public GpuResourceUse AcquireUse(GpuResourceRef resource) { CheckOpen(); var use = new GpuResourceUse(Check(resource)); owners++; return use; }
    public GpuResourceBatch BeginBatch()
    { CheckOpen(); var batch = new GpuResourceBatch(this); batches.Add(batch); owners++; return batch; }
    public GpuBufferRange GetBufferRange(GpuBufferRef buffer, ulong offset = 0, ulong? length = null)
    { Check(buffer); return new GpuBufferRange(buffer.Lease.Handle, offset, length).Normalize(buffer.Description); }
    public GpuTextureHandle GetTextureHandle(GpuTextureRef texture) { Check(texture); return texture.Lease.Handle; }
    public GpuTextureView GetTextureView(GpuViewRef view) { Check(view); return view.View; }
    public GpuBindingsHandle GetBindingsHandle(GpuBindingsRef resource) { Check(resource); return resource.Handle; }
    internal GpuBufferRef CreateBuffer(GpuBufferDescription description)
    {
        records.EnsureCapacity(records.Count + 1);
        var record = new ResourceRecord(this);
        GpuBufferLease lease = buffers.Acquire(description);
        try
        {
            var reference = new GpuBufferRef(record, lease);
            record.Reference = reference; record.Destroy = () => buffers.Release(lease); records.Add(record); return reference;
        }
        catch { buffers.Release(lease); throw; }
    }
    internal GpuTextureRef CreateTexture(GpuTextureDescription description)
    {
        records.EnsureCapacity(records.Count + 1);
        var record = new ResourceRecord(this);
        GpuTextureLease lease = textures.Acquire(description);
        try
        {
            var reference = new GpuTextureRef(record, lease);
            record.Reference = reference; record.Destroy = () => textures.Release(lease); records.Add(record); return reference;
        }
        catch { textures.Release(lease); throw; }
    }
    internal GpuViewRef GetView(GpuTextureRef texture, GpuTextureViewDescription description)
    {
        ResourceRecord textureRecord = Check(texture);
        description = description.Normalize(texture.Description);
        var key = (textureRecord, description);
        if (views.TryGetValue(key, out GpuViewRef? existing)) { return existing; }
        bool isDefault = description == default(GpuTextureViewDescription).Normalize(texture.Description);
        var record = isDefault ? textureRecord : new ResourceRecord(this);
        var reference = new GpuViewRef(record, new(texture.Lease.Handle, description));
        if (!isDefault) { Register(record, reference, [textureRecord]); }
        views.Add(key, reference);
        record.RemoveCache = () => views.Remove(key);
        return reference;
    }
    internal GpuSamplerRef GetSampler(GpuSamplerDescription description)
    {
        if (samplers.TryGetValue(description, out GpuSamplerRef? existing)) { return existing; }
        var record = new ResourceRecord(this);
        var reference = new GpuSamplerRef(record, description);
        Register(record, reference, []); samplers.Add(description, reference);
        record.RemoveCache = () => samplers.Remove(description); return reference;
    }
    internal GpuBindingsRef GetBindings(PortableShaderProgram program, uint group, IGpuBindingInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(inputs);
        if (group >= program.BindingLayouts.Count) { throw new ArgumentOutOfRangeException(nameof(group)); }
        var writer = new GpuBindingWriter(this); inputs.Write(writer);
        ManagedBindingEntry[] entries = writer.Finish();
        var key = new BindingCacheKey(program.BindingLayouts[(int)group], entries);
        if (bindings.TryGetValue(key, out GpuBindingsRef? existing)) { return existing; }
        var record = new ResourceRecord(this);
        ResourceRecord[] dependencies = entries.Select(item => item.Resource).Distinct().ToArray();
        records.EnsureCapacity(records.Count + 1);
        GpuBindingsHandle handle = Backend.CreateBindings(key.Layout, entries.Select(item => item.Entry).ToArray());
        try
        {
            var reference = new GpuBindingsRef(record, handle);
            record.Destroy = () => Backend.DestroyBindings(handle);
            record.RemoveCache = () => bindings.Remove(key);
            bindings.Add(key, reference); Register(record, reference, dependencies); return reference;
        }
        catch { Backend.DestroyBindings(handle); throw; }
    }
    private void Register(ResourceRecord record, GpuResourceRef reference, ResourceRecord[] dependencies)
    {
        records.EnsureCapacity(records.Count + 1);
        record.Reference = reference; record.Dependencies = dependencies;
        records.Add(record);
        foreach (ResourceRecord dependency in dependencies) { dependency.Holds++; }
    }
    internal void AddDependency(ResourceRecord record, ResourceRecord dependency)
    {
        if (record.Dependencies.Contains(dependency)) { return; }
        var pending = new Stack<ResourceRecord>(); var visited = new HashSet<ResourceRecord>(); pending.Push(dependency);
        while (pending.TryPop(out ResourceRecord? next))
        {
            if (ReferenceEquals(next, record)) { throw new ArgumentException("The explicit resource dependency would create a cycle.", nameof(dependency)); }
            if (!visited.Add(next)) { continue; }
            foreach (ResourceRecord item in next.Dependencies) { pending.Push(item); }
        }
        ResourceRecord[] dependencies = [.. record.Dependencies, dependency];
        record.Dependencies = dependencies; dependency.Holds++;
    }
    internal ManagedSubmission PrepareSubmission()
    {
        CheckOpen();
        ulong signal = checked(nextSignal + 1);
        var submission = new ManagedSubmission(this, new(semaphore, signal));
        submissions.Add(submission); nextSignal = signal; return submission;
    }
    internal void Retire(ResourceRecord record, GpuSubmissionToken token)
    {
        CheckOpen();
        ManagedSubmission submission = token.Submission ?? throw new ArgumentException("A valid manager submission token is required.", nameof(token));
        if (!ReferenceEquals(submission.Manager, this)) { throw new ArgumentException("The submission belongs to another manager.", nameof(token)); }
        retirements.Add((record, submission));
    }
    /// <summary>Reclaims only explicitly released ownership whose GPU uses are known to have ended. Does not block.</summary>
    public void Collect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var errors = new List<Exception>();
        foreach (ManagedSubmission submission in submissions)
        {
            if (submission.Ended) { continue; }
            try { submission.Poll(); } catch (Exception error) { errors.Add(ManagedSubmission.Sanitize(error)); }
        }
        for (int index = retirements.Count - 1; index >= 0; index--)
        {
            if (!retirements[index].Submission.Ended) { continue; }
            retirements[index].Record.Holds--; retirements.RemoveAt(index);
        }
        foreach (GpuResourceBatch batch in batches)
        {
            if (!batch.EndRequested || batch.Cleaned || batch.Submission is { Ended: false }) { continue; }
            try { batch.Cleanup(); } catch (Exception error) { errors.Add(error); }
        }
        batches.RemoveAll(batch => batch.Cleaned);
        bool progress;
        do
        {
            progress = false;
            foreach (ResourceRecord record in records)
            {
                if (!record.Alive || record.Holds != 0) { continue; }
                record.Alive = false; record.DestroyAttempted = true; record.RemoveCache?.Invoke();
                try
                {
                    record.Destroy();
                    foreach (ResourceRecord dependency in record.Dependencies) { dependency.Holds--; }
                    record.Dependencies = []; progress = true;
                }
                catch (Exception error) { record.DestroyError = error; }
            }
        } while (progress);
        foreach (ResourceRecord record in records)
        { if (record.DestroyError is { } error) { errors.Add(error); } }
        records.RemoveAll(record => !record.Alive && record.DestroyError is null);
        submissions.RemoveAll(submission => submission.Ended && submission.Outcome.Task.IsCompleted
            && (submission.Outcome.Task.IsCompletedSuccessfully || submission.OutcomeObserved));
        ThrowErrors(errors);
    }
    public void Trim()
    {
        Collect();
        var errors = new List<Exception>();
        try { buffers.Trim(); } catch (Exception error) { errors.Add(error); }
        try { textures.Trim(); } catch (Exception error) { errors.Add(error); }
        ThrowErrors(errors);
    }
    /// <summary>Waits for the current managed submission snapshot, then collects known-ended uses; cancellation retains pending ownership.</summary>
    public async ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ManagedSubmission[] pending = submissions.ToArray();
        var errors = new List<Exception>();
        try
        {
            var remaining = pending.ToHashSet();
            // A failed observation must not be hidden indefinitely behind a different unfinished GPU point.
            while (remaining.Count != 0)
            {
                Task completed = await Task.WhenAny(remaining.Select(item => item.Outcome.Task)).WaitAsync(cancellationToken);
                ManagedSubmission submission = remaining.Single(item => ReferenceEquals(item.Outcome.Task, completed));
                await submission.WaitAsync(cancellationToken); remaining.Remove(submission);
            }
        }
        catch (Exception error) { errors.Add(error); }
        try { Collect(); } catch (Exception error) { errors.Add(error); }
        ThrowErrors(errors);
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) { return; }
        if (owners != 0) { throw new InvalidOperationException("Dispose scopes, pins, uses and batches before disposing their manager."); }
        ending = true;
        await WaitIdleAsync();
        if (records.Count != 0 || retirements.Count != 0 || batches.Count != 0)
        { throw new InvalidOperationException("Uncertain resource use or failed cleanup still retains manager ownership."); }
        // Idle pools contain only known-ended resources. Backend destruction is never used as completion evidence.
        var errors = new List<Exception>();
        try { buffers.Dispose(); } catch (Exception error) { errors.Add(error); }
        try { textures.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 0) { semaphore.Dispose(); disposed = true; }
        ThrowErrors(errors);
    }
    public GpuResourceStatistics Statistics
    {
        get
        {
            ResourceRecord[] live = records.Where(record => record.Alive).ToArray();
            return new(live.Length, live.Count(item => item.Reference is GpuBufferRef), live.Count(item => item.Reference is GpuTextureRef),
                live.Count(item => item.Reference is GpuViewRef), live.Count(item => item.Reference is GpuSamplerRef),
                live.Count(item => item.Reference is GpuBindingsRef), live.Count(item => item.Reference is GpuPackageRef),
                live.Where(item => item.Reference is GpuBufferRef).Aggregate(0UL, (sum, item) => checked(sum + ((GpuBufferRef)item.Reference).Description.Size)),
                submissions.Count(item => !item.Ended), records.Count(item => item.DestroyError is not null));
        }
    }
    private static void ThrowErrors(List<Exception> errors)
    {
        if (errors.Count == 1) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Count > 1) { throw new AggregateException(errors); }
    }
}
