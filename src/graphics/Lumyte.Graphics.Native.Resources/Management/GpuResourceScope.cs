namespace Lumyte.Graphics.Native.Resources;

public sealed class GpuResourceScope : IDisposable
{
    internal GpuResourceScope(GpuResourceManager manager) => Manager = manager;
    internal GpuResourceManager Manager { get; }
    internal readonly HashSet<ResourceRecord> Records = [];
    internal bool Closed;
    internal bool Owned;
    internal void RequireOpen()
    {
        Manager.RequireOpen();
        if (Closed || Owned) { throw new InvalidOperationException("The resource scope is closed or owned by a batch."); }
    }
    internal T Hold<T>(T reference) where T : GpuResourceRef
    {
        RequireOpen();
        ResourceRecord record = Manager.Require(reference);
        if (Records.Add(record)) { record.Holds++; }
        return reference;
    }
    public GpuBufferRef CreateBuffer(GpuBufferDescription description) { RequireOpen(); return Hold(Manager.CreateBuffer(description)); }
    /// <summary>Imports borrowed memory and transfers its explicit lifetime lease to this scope.</summary>
    public GpuBufferRef ImportBuffer(NativeGpuRange range, IDisposable lease)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(lease);
        ResourceRecord record = Manager.Register(lease.Dispose); record.Buffer = range;
        return Hold(new GpuBufferRef(record));
    }
    /// <summary>Imports a borrowed texture and transfers its explicit lifetime lease to this scope.</summary>
    public GpuTextureRef ImportTexture(NativeGpuTextureHandle texture, NativeGpuTextureDescription description, IDisposable lease)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(texture); ArgumentNullException.ThrowIfNull(lease);
        ResourceRecord record = Manager.Register(lease.Dispose); record.Texture = texture; record.TextureDescription = description;
        return Hold(new GpuTextureRef(record));
    }
    public GpuTextureRef CreateTexture(NativeGpuTextureDescription description,
        NativeGpuMemoryKind memoryKind = NativeGpuMemoryKind.GpuOnly, GpuTextureViewDescription? defaultView = null)
    { RequireOpen(); return Hold(Manager.CreateTexture(description, memoryKind, defaultView)); }
    public GpuViewRef GetView(GpuTextureRef texture, GpuTextureViewDescription description)
    { RequireOpen(); return Hold(Manager.GetView(texture, description)); }
    public GpuViewRef GetView(GpuBufferRef buffer, GpuBufferViewDescription description)
    { RequireOpen(); return Hold(Manager.GetView(buffer, description)); }
    public GpuSamplerRef GetSampler(NativeGpuSamplerDescription description)
    { RequireOpen(); return Hold(Manager.GetSampler(description)); }
    public void AddDependency(GpuResourceRef resource, GpuResourceRef dependency)
    {
        RequireOpen();
        ResourceRecord record = Manager.Require(resource);
        if (!Records.Contains(record)) { throw new ArgumentException("The scope does not hold the dependent resource.", nameof(resource)); }
        Manager.AddDependency(record, Manager.Require(dependency));
    }
    public void Release(GpuResourceRef reference)
    {
        RequireOpen();
        ResourceRecord record = Manager.Require(reference);
        if (!Records.Remove(record)) { throw new ArgumentException("The scope does not hold this resource.", nameof(reference)); }
        record.Holds--;
    }
    public Task<GpuPackageRef> ImportPackageAsync(GpuPackagePlan plan, CancellationToken cancellationToken = default)
        => ImportPackageAsync(plan, GpuPackagePlacement.Pools, cancellationToken);
    public Task<GpuPackageRef> ImportPackageAsync(GpuPackagePlan plan, GpuPackagePlacement placement,
        CancellationToken cancellationToken = default)
    { RequireOpen(); return Manager.ImportPackageAsync(this, plan, placement, cancellationToken); }
    public void Dispose()
    {
        if (Closed) { return; }
        if (Owned) { throw new InvalidOperationException("The batch owns this scope."); }
        Close();
    }
    internal void Close()
    {
        if (Closed) { return; }
        Closed = true;
        foreach (ResourceRecord record in Records) { record.Holds--; }
        Records.Clear();
        Manager.Scopes.Remove(this);
    }
}

public sealed class GpuResourcePin : IDisposable
{
    private GpuResourceUse? use;
    internal GpuResourcePin(GpuResourceUse use) => this.use = use;
    public void Dispose() { use?.Dispose(); use = null; }
    internal ResourceRecord TransferToBatch(GpuResourceManager manager)
        => use?.TransferToBatch(manager) ?? throw new InvalidOperationException("The pin has been returned.");
    internal void ReturnFromBatch() { use?.ReturnFromBatch(); use = null; }
}

public sealed class GpuResourceUse : IDisposable
{
    private readonly GpuResourceManager manager;
    private ResourceRecord? record;
    private bool batchOwned;
    private bool external = true;
    internal GpuResourceUse(GpuResourceManager manager, ResourceRecord record)
    { this.manager = manager; this.record = record; record.Holds++; manager.ExternalUses++; }
    public void Dispose()
    {
        if (batchOwned) { throw new InvalidOperationException("The batch owns this use."); }
        if (record is null) { return; }
        record.Holds--;
        record = null;
        if (external) { manager.ExternalUses--; }
    }
    internal ResourceRecord TransferToBatch(GpuResourceManager owner)
    {
        if (!ReferenceEquals(owner, manager)) { throw new ArgumentException("The use belongs to another manager.", nameof(owner)); }
        if (batchOwned || record is null) { throw new InvalidOperationException("This use has already been transferred or returned."); }
        batchOwned = true; external = false; manager.ExternalUses--; return record;
    }
    internal void ReturnFromBatch() { batchOwned = false; Dispose(); }
    public void Retire(GpuSubmissionToken completion)
    {
        if (batchOwned || record is null) { throw new InvalidOperationException("This use has already been transferred, returned or retired."); }
        Submission submission = manager.Require(completion);
        if (submission.Ended.IsCompletedSuccessfully) { Dispose(); return; }
        submission.Retired.EnsureCapacity(checked(submission.Retired.Count + 1));
        submission.Retired.Add(record);
        record = null;
        manager.ExternalUses--;
    }
}
