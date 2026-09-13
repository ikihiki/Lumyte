using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;
using NativeBufferDescription = Lumyte.Graphics.Native.Resources.GpuBufferDescription;
using NativeTextureViewDescription = Lumyte.Graphics.Native.Resources.GpuTextureViewDescription;

namespace Lumyte.Graphics.Native.RenderGraph;

public abstract class NativePassResource
{
    private protected NativePassResource(string name) => Name = name;
    public string Name { get; }
    internal abstract GpuResourceRef Reference { get; }
}

public sealed class NativePassBuffer : NativePassResource
{
    internal NativePassBuffer(string name, NativeBufferDescription description, GpuBufferRef? reference = null) : base(name)
    { Description = description; Value = reference; }
    public NativeBufferDescription Description { get; }
    internal GpuBufferRef? Value;
    internal override GpuResourceRef Reference => Value ?? throw new InvalidOperationException("The buffer has not been allocated.");
}

public sealed class NativePassTexture : NativePassResource
{
    internal NativePassTexture(string name, NativeGpuTextureDescription description, GpuTextureRef? reference = null) : base(name)
    { Description = description; Value = reference; Initialized = reference is not null; }
    public NativeGpuTextureDescription Description { get; internal set; }
    internal GpuTextureRef? Value;
    internal bool Initialized;
    internal GpuTextureLayout Layout = GpuTextureLayout.General;
    internal override GpuResourceRef Reference => Value ?? throw new InvalidOperationException("The texture has not been allocated.");
}

public sealed class NativePassView
{
    internal NativePassView(NativePassTexture texture, NativeTextureViewDescription description)
    { Texture = texture; Description = description; }
    public NativePassTexture Texture { get; }
    public NativeTextureViewDescription Description { get; }
    internal GpuViewRef? Value;
}

public readonly record struct NativePassUsage(GpuStage Stages, GpuAccess Access);
public delegate void NativePassRecordAction<in TState>(NativePassRecordContext context, TState state);

public sealed class NativePassBuilder
{
    private bool closed;
    internal NativePassBuilder(string name, Action<NativePassRecordContext> record) { Name = name; Record = record; }
    public string Name { get; }
    internal Action<NativePassRecordContext> Record { get; }
    internal Dictionary<NativePassResource, NativePassUsage> Uses { get; } = [];
    public NativePassBuilder Read(NativePassResource resource, NativePassUsage usage) => Use(resource, usage);
    public NativePassBuilder Write(NativePassResource resource, NativePassUsage usage) => Use(resource, usage);
    public NativePassBuilder ReadWrite(NativePassResource resource, NativePassUsage usage) => Use(resource, usage);
    private NativePassBuilder Use(NativePassResource resource, NativePassUsage usage)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        ArgumentNullException.ThrowIfNull(resource);
        if (Uses.TryGetValue(resource, out NativePassUsage previous))
        { usage = new(previous.Stages | usage.Stages, previous.Access | usage.Access); }
        Uses[resource] = usage; return this;
    }
    internal void Seal() => closed = true;
}

public sealed class NativePassRecordContext
{
    private readonly GpuResourceManager resources;
    internal NativePassRecordContext(NativeGpuCommandBuffer commands, GpuResourceManager resources)
    { Commands = commands; this.resources = resources; }
    public NativeGpuCommandBuffer Commands { get; }
    public NativeGpuRange GetBufferRange(NativePassBuffer buffer, ulong offset = 0, ulong? length = null)
        => resources.GetBufferRange((GpuBufferRef)buffer.Reference, offset, length);
    public NativeGpuTextureHandle GetTexture(NativePassTexture texture) => resources.GetTextureHandle((GpuTextureRef)texture.Reference);
    public NativeGpuTextureView GetTextureView(NativePassView view) => resources.GetTextureView(Require(view));
    public NativeGpuRenderViewHandle GetRenderView(NativePassView view) => resources.GetRenderViewHandle(Require(view));
    public uint GetShaderIndex(NativePassView view) => resources.GetShaderIndex(Require(view));
    private static GpuViewRef Require(NativePassView view) => view.Value ?? throw new InvalidOperationException("The view has not been prepared.");
}
