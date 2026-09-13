namespace Lumyte.Graphics.Portable.Resources;

/// <summary>A non-owning resource identity. Copying this reference does not retain its resource.</summary>
public abstract class GpuResourceRef
{
    internal GpuResourceRef(ResourceRecord record) { Record = record; }
    internal ResourceRecord Record { get; }
}

public sealed class GpuBufferRef : GpuResourceRef
{
    internal GpuBufferRef(ResourceRecord record, GpuBufferLease lease) : base(record) { Lease = lease; }
    internal GpuBufferLease Lease { get; }
    public GpuBufferDescription Description => Lease.Description;
}

public sealed class GpuTextureRef : GpuResourceRef
{
    internal GpuTextureRef(ResourceRecord record, GpuTextureLease lease) : base(record) { Lease = lease; }
    internal GpuTextureLease Lease { get; }
    public GpuTextureDescription Description => Lease.Description;
}

public sealed class GpuViewRef : GpuResourceRef
{
    internal GpuViewRef(ResourceRecord record, GpuTextureView view) : base(record) { View = view; }
    internal GpuTextureView View { get; }
    public GpuTextureViewDescription Description => View.Description;
}

public sealed class GpuSamplerRef : GpuResourceRef
{
    internal GpuSamplerRef(ResourceRecord record, GpuSamplerDescription description) : base(record) { Description = description; }
    public GpuSamplerDescription Description { get; }
}

public sealed class GpuBindingsRef : GpuResourceRef
{
    internal GpuBindingsRef(ResourceRecord record, GpuBindingsHandle handle) : base(record) { Handle = handle; }
    internal GpuBindingsHandle Handle { get; }
}

public sealed class GpuPackageRef : GpuResourceRef
{
    private readonly IReadOnlyDictionary<string, GpuResourceRef> exports;
    internal GpuPackageRef(ResourceRecord record, IReadOnlyDictionary<string, GpuResourceRef> exports) : base(record)
    { this.exports = exports; }
    public GpuResourceRef GetExport(string id)
    {
        Record.Manager.Check(this);
        return exports.TryGetValue(id, out GpuResourceRef? resource) ? resource : throw new KeyNotFoundException($"Package export '{id}' was not found.");
    }
    public T GetExport<T>(string id) where T : GpuResourceRef => GetExport(id) as T
        ?? throw new InvalidOperationException($"Package export '{id}' does not have type {typeof(T).Name}.");
}

internal sealed class ResourceRecord(GpuResourceManager manager)
{
    internal GpuResourceManager Manager { get; } = manager;
    internal GpuResourceRef Reference { get; set; } = null!;
    internal int Holds { get; set; }
    internal bool Alive { get; set; } = true;
    internal bool DestroyAttempted { get; set; }
    internal Exception? DestroyError { get; set; }
    internal Action Destroy { get; set; } = static () => { };
    internal Action? RemoveCache { get; set; }
    internal ResourceRecord[] Dependencies { get; set; } = [];
}
