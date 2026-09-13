namespace Lumyte.Graphics.Native.Resources;

/// <summary>An immutable, non-owning reference to one manager generation.</summary>
public abstract class GpuResourceRef
{
    internal GpuResourceRef(ResourceRecord record) => Record = record;
    internal ResourceRecord Record { get; }
}

public sealed class GpuBufferRef : GpuResourceRef { internal GpuBufferRef(ResourceRecord record) : base(record) { } }
public sealed class GpuTextureRef : GpuResourceRef { internal GpuTextureRef(ResourceRecord record) : base(record) { } }
public sealed class GpuViewRef : GpuResourceRef { internal GpuViewRef(ResourceRecord record) : base(record) { } }
public sealed class GpuSamplerRef : GpuResourceRef { internal GpuSamplerRef(ResourceRecord record) : base(record) { } }
public sealed class GpuPackageRef : GpuResourceRef
{
    internal GpuPackageRef(ResourceRecord record, IReadOnlyDictionary<string, GpuResourceRef> exports) : base(record) => Exports = exports;
    private IReadOnlyDictionary<string, GpuResourceRef> Exports { get; }
    public GpuResourceRef GetExport(string id) { Record.Owner.Require( this); return Exports[id]; }
    public T GetExport<T>(string id) where T : GpuResourceRef => GetExport(id) as T
        ?? throw new ArgumentException("The export has a different resource type.", nameof(id));
}

internal sealed class ResourceRecord(GpuResourceManager owner, Action destroy)
{
    internal GpuResourceManager Owner { get; } = owner;
    internal Action Destroy { get; } = destroy;
    internal readonly HashSet<ResourceRecord> Dependencies = [];
    internal int Holds;
    internal bool Alive = true;
    internal Exception? Failure;
    internal NativeGpuRange? Buffer;
    internal NativeGpuTextureHandle? Texture;
    internal NativeGpuTextureDescription? TextureDescription;
    internal NativeGpuTextureView? TextureView;
    internal NativeGpuRenderViewHandle? RenderView;
    internal uint? ShaderIndex;
}
