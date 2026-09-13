using System.Collections.ObjectModel;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Opaque non-owning resource identity. Providers derive this type to store their own representation.</summary>
public abstract class GpuGraphResourceRef
{
    protected GpuGraphResourceRef(Guid runtimeId, Guid identity) { RuntimeId = runtimeId; Identity = identity; }
    public Guid RuntimeId { get; }
    public Guid Identity { get; }
}

public abstract class GpuGraphTextureRef : GpuGraphResourceRef
{
    protected GpuGraphTextureRef(Guid runtimeId, Guid identity, GpuGraphTextureDescription description)
        : base(runtimeId, identity) => Description = description;
    public GpuGraphTextureDescription Description { get; }
}

public abstract class GpuGraphBufferRef : GpuGraphResourceRef
{
    protected GpuGraphBufferRef(Guid runtimeId, Guid identity, GpuGraphBufferDescription description)
        : base(runtimeId, identity) => Description = description;
    public GpuGraphBufferDescription Description { get; }
}

public sealed class GpuGraphPackageRef : GpuGraphResourceRef
{
    private readonly IReadOnlyDictionary<string, GpuGraphResourceRef> exports;
    public GpuGraphPackageRef(Guid runtimeId, Guid identity, IReadOnlyDictionary<string, GpuGraphResourceRef> exports)
        : base(runtimeId, identity) => this.exports = new ReadOnlyDictionary<string, GpuGraphResourceRef>(new Dictionary<string, GpuGraphResourceRef>(exports));
    public GpuGraphBufferRef GetBuffer(string exportId) => exports[exportId] as GpuGraphBufferRef
        ?? throw new ArgumentException("The export is not a buffer.", nameof(exportId));
    public GpuGraphTextureRef GetTexture(string exportId) => exports[exportId] as GpuGraphTextureRef
        ?? throw new ArgumentException("The export is not a texture.", nameof(exportId));
}

public interface IGpuGraphResources
{
    GpuGraphResourceScope CreateScope();
    GpuGraphResourcePin Pin(GpuGraphResourceRef reference);
    /// <summary>Provider/presentation extension: synchronously acquire use before any asynchronous suspension.</summary>
    IDisposable AcquireUse(GpuGraphResourceRef reference);
    void Collect();
    void Trim();
}

public abstract class GpuGraphResourceScope : IDisposable
{
    public abstract ValueTask<GpuGraphPackageRef> ImportPackageAsync(GpuPackageUploadData data, CancellationToken cancellationToken = default);
    public abstract void Release(GpuGraphResourceRef reference);
    public abstract void Dispose();
}

public abstract class GpuGraphResourcePin : IDisposable
{
    public abstract void Dispose();
}
