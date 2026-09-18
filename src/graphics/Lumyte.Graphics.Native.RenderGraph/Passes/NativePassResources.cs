using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;
using NativeBufferDescription = Lumyte.Graphics.Native.Resources.GpuBufferDescription;
using NativeTextureViewDescription = Lumyte.Graphics.Native.Resources.GpuTextureViewDescription;

namespace Lumyte.Graphics.Native.RenderGraph;

public abstract class NativePassResource
{
    private protected NativePassResource(NativeExecutionBuild owner, string name, bool imported)
    { Owner = owner; Name = name; IsImported = imported; }
    internal NativeExecutionBuild Owner { get; }
    public string Name { get; }
    internal bool IsImported { get; }
    internal abstract GpuResourceRef Reference { get; }
}

public sealed class NativePassBuffer : NativePassResource
{
    internal NativePassBuffer(NativeExecutionBuild owner, string name, NativeBufferDescription description, GpuBufferRef? reference = null)
        : base(owner, name, reference is not null) { Description = description; Value = reference; }
    public NativeBufferDescription Description { get; }
    internal GpuBufferRef? Value;
    internal override GpuResourceRef Reference => Value ?? throw new InvalidOperationException("The buffer has not been allocated.");
}

public sealed class NativePassTexture : NativePassResource
{
    internal NativePassTexture(NativeExecutionBuild owner, string name, NativeGpuTextureDescription description, GpuTextureRef? reference = null, bool discardContents = false)
        : base(owner, name, reference is not null) { Description = description; Value = reference; DiscardContents = discardContents; }
    internal bool DiscardContents { get; }
    public NativeGpuTextureDescription Description { get; internal set; }
    internal GpuTextureRef? Value;
    internal override GpuResourceRef Reference => Value ?? throw new InvalidOperationException("The texture has not been allocated.");
}

public sealed class NativePassView
{
    internal NativePassView(string name, NativePassTexture texture, NativeTextureViewDescription description)
    { Name = name; Texture = texture; Description = description; }
    public string Name { get; }
    public NativePassTexture Texture { get; }
    public NativeTextureViewDescription Description { get; }
    internal GpuViewRef? Value;
}

public readonly record struct NativePassUsage(GpuStage Stages, GpuAccess Access);
public delegate void NativePassRecordAction<in TState>(NativePassRecordContext context, TState state);

public sealed class NativePassBuilder
{
    private bool closed;
    internal NativePassBuilder(NativePassBuildContext feature, string name, Action<NativePassRecordContext> record)
    { Feature = feature; Name = name; Record = record; }
    internal NativePassBuildContext Feature { get; }
    public string Name { get; }
    internal Action<NativePassRecordContext> Record { get; }
    internal Dictionary<NativePassResource, NativePassUsage> Uses { get; } = [];
    internal Dictionary<NativePassResource, GpuRenderGraphAccess> Accesses { get; } = [];
    internal HashSet<NativePassBuilder> Dependencies { get; } = [];
    internal bool IsPreserved { get; private set; }
    public NativePassBuilder Read(NativePassResource resource, NativePassUsage usage) => Use(resource, usage, GpuRenderGraphAccess.Read);
    public NativePassBuilder Write(NativePassResource resource, NativePassUsage usage) => Use(resource, usage, GpuRenderGraphAccess.Write);
    public NativePassBuilder ReadWrite(NativePassResource resource, NativePassUsage usage) => Use(resource, usage, GpuRenderGraphAccess.ReadWrite);
    public NativePassBuilder Preserve()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        if (!Feature.Declaration.IsPreserved) { throw new InvalidOperationException("A side effect must be preserved by the common feature contract."); }
        IsPreserved = true; return this;
    }
    private NativePassBuilder Use(NativePassResource resource, NativePassUsage usage, GpuRenderGraphAccess access)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        Feature.RequireResource(resource);
        if (usage.Stages == 0 || usage.Access == 0) { throw new ArgumentException("A resource use requires execution and memory access scopes.", nameof(usage)); }
        if (Uses.TryGetValue(resource, out NativePassUsage previous))
        { usage = new(previous.Stages | usage.Stages, previous.Access | usage.Access); }
        if (Accesses.TryGetValue(resource, out GpuRenderGraphAccess previousAccess) && previousAccess != access)
        { access = GpuRenderGraphAccess.ReadWrite; }
        Uses[resource] = usage; Accesses[resource] = access; return this;
    }
    internal void Seal() => closed = true;
}

public sealed class NativePassRecordContext
{
    private readonly NativeGpuCommandBuffer commands;
    private readonly GpuResourceManager resources;
    private readonly NativePassBuilder pass;
    private bool closed;
    internal NativePassRecordContext(NativeGpuCommandBuffer commands, GpuResourceManager resources, NativePassBuilder pass)
    { this.commands = commands; this.resources = resources; this.pass = pass; }
    public NativeGpuCommandBuffer Commands { get { RequireOpen(); return commands; } }
    public NativeGpuRange GetBufferRange(NativePassBuffer buffer, ulong offset = 0, ulong? length = null)
    { Require(buffer); return resources.GetBufferRange((GpuBufferRef)buffer.Reference, offset, length); }
    public NativeGpuTextureHandle GetTexture(NativePassTexture texture)
    { Require(texture); return resources.GetTextureHandle((GpuTextureRef)texture.Reference); }
    public NativeGpuTextureView GetTextureView(NativePassView view) => resources.GetTextureView(Require(view));
    public NativeGpuRenderViewHandle GetRenderView(NativePassView view) => resources.GetRenderViewHandle(Require(view));
    public uint GetShaderIndex(NativePassView view) => resources.GetShaderIndex(Require(view));
    private GpuViewRef Require(NativePassView view)
    { ArgumentNullException.ThrowIfNull(view); Require(view.Texture); return view.Value ?? throw new InvalidOperationException("The view has not been prepared."); }
    private void Require(NativePassResource resource)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(resource);
        if (!pass.Uses.ContainsKey(resource)) { throw new ArgumentException("The recording did not declare this resource.", nameof(resource)); }
    }
    private void RequireOpen() => ObjectDisposedException.ThrowIf(closed, this);
    internal void Close() => closed = true;
}
