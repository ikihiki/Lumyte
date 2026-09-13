using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.Resources;

/// <summary>Explicit CPU ownership of resources. Disposing the scope does not cancel submitted GPU uses.</summary>
public sealed class GpuResourceScope : IDisposable
{
    internal readonly GpuResourceManager Manager;
    internal readonly HashSet<ResourceRecord> Records = [];
    private bool closed;
    internal GpuResourceScope(GpuResourceManager manager) { Manager = manager; }
    internal void CheckOpen() { ObjectDisposedException.ThrowIf(closed, this); Manager.CheckOpen(); }
    internal T Hold<T>(T resource) where T : GpuResourceRef
    {
        CheckOpen();
        ResourceRecord record = Manager.Check(resource);
        if (Records.Add(record)) { record.Holds++; }
        return resource;
    }
    public GpuBufferRef CreateBuffer(GpuBufferDescription description) { CheckOpen(); return Hold(Manager.CreateBuffer(description)); }
    public GpuTextureRef CreateTexture(GpuTextureDescription description) { CheckOpen(); return Hold(Manager.CreateTexture(description)); }
    /// <summary>Imports a raw object from this backend and transfers its explicit ownership lease on success. No pooling or hidden copy occurs.</summary>
    public GpuBufferRef ImportBuffer(GpuBufferHandle handle, GpuBufferDescription description, IDisposable lease)
    { CheckOpen(); return Hold(Manager.ImportBuffer(handle, description, lease)); }
    /// <summary>Imports a raw object from this backend and transfers its explicit ownership lease on success. No pooling or hidden copy occurs.</summary>
    public GpuTextureRef ImportTexture(GpuTextureHandle handle, GpuTextureDescription description, IDisposable lease)
    { CheckOpen(); return Hold(Manager.ImportTexture(handle, description, lease)); }
    public GpuViewRef GetView(GpuTextureRef texture, GpuTextureViewDescription description = default)
    { CheckOpen(); return Hold(Manager.GetView(texture, description)); }
    public GpuSamplerRef GetSampler() => GetSampler(new GpuSamplerDescription());
    public GpuSamplerRef GetSampler(GpuSamplerDescription description)
    { CheckOpen(); return Hold(Manager.GetSampler(description)); }
    public GpuBindingsRef GetBindings(PortableShaderProgram program, uint group, IGpuBindingInputs inputs)
    { CheckOpen(); return Hold(Manager.GetBindings(program, group, inputs)); }
    public void Release(GpuResourceRef resource)
    {
        CheckOpen();
        ResourceRecord record = Manager.Check(resource);
        if (!Records.Remove(record)) { throw new InvalidOperationException("This scope does not own the resource."); }
        record.Holds--;
    }
    /// <summary>Keeps an explicit dependency alive as long as the resource is alive. GPU inputs are never scanned.</summary>
    public void AddDependency(GpuResourceRef resource, GpuResourceRef dependency)
    {
        CheckOpen();
        ResourceRecord record = Manager.Check(resource);
        if (!Records.Contains(record)) { throw new InvalidOperationException("This scope does not own the resource."); }
        Manager.AddDependency(record, Manager.Check(dependency));
    }
    public ValueTask<GpuPackageRef> ImportPackageAsync(GpuPackagePlan plan, CancellationToken cancellationToken = default)
    { CheckOpen(); return Manager.ImportPackageAsync(this, plan, cancellationToken); }
    internal ResourceRecord[] Snapshot() { CheckOpen(); return Records.ToArray(); }
    internal ResourceRecord[] Transfer()
    {
        ResourceRecord[] records = Snapshot();
        closed = true;
        Records.Clear();
        Manager.CloseOwner();
        return records;
    }
    public void Dispose()
    {
        if (closed) { return; }
        closed = true;
        foreach (ResourceRecord record in Records) { record.Holds--; }
        Records.Clear();
        Manager.CloseOwner();
    }
}

/// <summary>An explicit CPU hold on a resource and its dependencies.</summary>
public sealed class GpuResourcePin : IDisposable, IManagedBatchLease
{
    private ResourceRecord? record;
    private bool transferred;
    internal GpuResourcePin(ResourceRecord record) { this.record = record; record.Holds++; }
    public void Dispose()
    {
        if (transferred) { throw new InvalidOperationException("The pin belongs to a batch."); }
        ResourceRecord? owned = record;
        if (owned is null) { return; }
        record = null; owned.Holds--; owned.Manager.CloseOwner();
    }
    ResourceRecord IManagedBatchLease.PrepareTransfer(GpuResourceManager manager)
    {
        ResourceRecord owned = record ?? throw new ObjectDisposedException(nameof(GpuResourcePin));
        if (transferred) { throw new InvalidOperationException("The pin already belongs to a batch."); }
        if (!ReferenceEquals(owned.Manager, manager)) { throw new ArgumentException("The pin belongs to another manager."); }
        return owned;
    }
    void IManagedBatchLease.Transfer() { transferred = true; record!.Manager.CloseOwner(); }
    void IManagedBatchLease.ReleaseFromBatch()
    {
        ResourceRecord? owned = record; if (owned is null) { return; }
        record = null; owned.Holds--;
    }
}

/// <summary>A raw-use hold. Dispose only after unsubmitted recordings or all GPU uses have ended.</summary>
public sealed class GpuResourceUse : IDisposable, IManagedBatchLease
{
    private ResourceRecord? record;
    private bool externalOwner = true;
    internal GpuResourceUse(ResourceRecord record) { this.record = record; record.Holds++; }
    public void Retire(GpuSubmissionToken token)
    {
        if (!externalOwner) { throw new InvalidOperationException("The use belongs to a batch."); }
        ResourceRecord owned = record ?? throw new ObjectDisposedException(nameof(GpuResourceUse));
        owned.Manager.Retire(owned, token);
        record = null;
        if (externalOwner) { owned.Manager.CloseOwner(); }
    }
    public void Dispose()
    {
        if (!externalOwner) { throw new InvalidOperationException("The use belongs to a batch."); }
        ResourceRecord? owned = record;
        if (owned is null) { return; }
        record = null; owned.Holds--; if (externalOwner) { owned.Manager.CloseOwner(); }
    }
    ResourceRecord IManagedBatchLease.PrepareTransfer(GpuResourceManager manager)
    {
        ResourceRecord owned = record ?? throw new ObjectDisposedException(nameof(GpuResourceUse));
        if (!ReferenceEquals(owned.Manager, manager)) { throw new ArgumentException("The mapped lease belongs to another manager."); }
        if (!externalOwner) { throw new InvalidOperationException("The mapping lease was already transferred to a batch."); }
        return owned;
    }
    void IManagedBatchLease.Transfer() { externalOwner = false; record!.Manager.CloseOwner(); }
    void IManagedBatchLease.ReleaseFromBatch()
    {
        ResourceRecord? owned = record; if (owned is null) { return; }
        record = null; owned.Holds--;
    }
}

internal interface IManagedBatchLease
{
    ResourceRecord PrepareTransfer(GpuResourceManager manager);
    void Transfer();
    void ReleaseFromBatch();
}
