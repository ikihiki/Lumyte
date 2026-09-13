using Lumyte.Graphics.Native.Resources;
using Lumyte.Graphics.RenderGraph;
using NativeBufferDescription = Lumyte.Graphics.Native.Resources.GpuBufferDescription;
using NativeTextureViewDescription = Lumyte.Graphics.Native.Resources.GpuTextureViewDescription;

namespace Lumyte.Graphics.Native.RenderGraph;

/// <summary>One feature build. Recording callbacks receive only the separately prepared record context.</summary>
public sealed class NativePassBuildContext
{
    private readonly NativeExecutionBuild build;
    private readonly GpuRenderGraphPass pass;
    private readonly List<NativePassBuilder> ownedPasses = [];
    private bool closed;
    internal NativePassBuildContext(NativeExecutionBuild build, GpuRenderGraphPass pass)
    { this.build = build; this.pass = pass; }
    public NativePassServices Services { get { RequireOpen(); return build.Services; } }
    public T GetInput<T>(GpuGraphValue<T> value) { RequireOpen(); return pass.GetInput(value, build.Bindings); }
    public NativePassTexture ImportTexture(GpuRenderGraphTexture resource)
    { RequireDeclared(resource); return (NativePassTexture)build.Resources[resource]; }
    public NativePassBuffer ImportBuffer(GpuRenderGraphBuffer resource)
    { RequireDeclared(resource); return (NativePassBuffer)build.Resources[resource]; }
    public NativePassTexture ImportTexture(GpuTextureRef reference, NativeGpuTextureDescription description)
    {
        RequireOpen();
        NativePassTexture texture = new("imported", description, reference);
        build.PrivateResources.Add(texture); return texture;
    }
    public NativePassBuffer ImportBuffer(GpuBufferRef reference)
    {
        RequireOpen();
        NativePassBuffer buffer = new("imported", new(Services.Resources.GetBufferRange(reference).Size), reference);
        build.PrivateResources.Add(buffer); return buffer;
    }
    public NativePassTexture CreateTexture(string name, NativeGpuTextureDescription description)
    { RequireOpen(); NativePassTexture texture = new(name, description); build.PrivateResources.Add(texture); return texture; }
    public NativePassBuffer CreateBuffer(string name, NativeBufferDescription description)
    { RequireOpen(); NativePassBuffer buffer = new(name, description); build.PrivateResources.Add(buffer); return buffer; }
    public NativePassView CreateView(string name, NativePassTexture texture, NativeTextureViewDescription description)
    { RequireOpen(); NativePassView view = new(texture, description); build.Views.Add(view); return view; }
    public NativePassBuilder AddPass<TState>(string name, TState state, NativePassRecordAction<TState> record)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(record);
        NativePassBuilder builder = new(name, context => record(context, state)); build.Passes.Add(builder); ownedPasses.Add(builder); return builder;
    }
    public void Retain(IDisposable lease)
    {
        RequireOpen(); ArgumentNullException.ThrowIfNull(lease);
        if (build.Leases.Any(existing => ReferenceEquals(existing, lease)))
        { throw new ArgumentException("This execution already owns the lease.", nameof(lease)); }
        build.Leases.Add(lease);
    }
    private void RequireDeclared(GpuRenderGraphResource resource)
    {
        RequireOpen();
        if (!pass.Uses.Any(use => ReferenceEquals(use.Resource, resource)))
        { throw new ArgumentException("The feature did not declare this resource.", nameof(resource)); }
    }
    private void RequireOpen() => ObjectDisposedException.ThrowIf(closed, this);
    internal void Close() { closed = true; foreach (NativePassBuilder builder in ownedPasses) { builder.Seal(); } }
}

internal sealed class NativeExecutionBuild(NativePassServices services, GpuRenderGraphBindings bindings)
{
    internal NativePassServices Services { get; } = services;
    internal GpuRenderGraphBindings Bindings { get; } = bindings;
    internal Dictionary<GpuRenderGraphResource, NativePassResource> Resources { get; } = [];
    internal List<NativePassResource> PrivateResources { get; } = [];
    internal List<NativePassView> Views { get; } = [];
    internal List<NativePassBuilder> Passes { get; } = [];
    internal List<IDisposable> Leases { get; } = [];

    internal IEnumerable<NativePassResource> AllResources => Resources.Values.Concat(PrivateResources);
    internal void Allocate(GpuResourceScope scope)
    {
        foreach (NativePassBuilder pass in Passes)
        {
            foreach ((NativePassResource resource, NativePassUsage usage) in pass.Uses)
            {
                if (resource is NativePassTexture texture && texture.Value is null)
                { texture.Description = texture.Description with { Usage = texture.Description.Usage | TextureUsage(usage.Access) }; }
            }
        }
        foreach (NativePassResource resource in AllResources)
        {
            if (resource is NativePassTexture texture) { texture.Value ??= scope.CreateTexture(texture.Description); }
            else if (resource is NativePassBuffer buffer) { buffer.Value ??= scope.CreateBuffer(buffer.Description); }
        }
        foreach (NativePassView view in Views)
        { view.Value = scope.GetView((GpuTextureRef)view.Texture.Reference, view.Description); }
    }
    internal void Record(NativeGpuCommandBuffer commands)
    {
        NativePassRecordContext context = new(commands, Services.Resources);
        bool explicitLayouts = Services.Backend.Capabilities.ExplicitTextureTransitions;
        NativePassUsage previous = new(GpuStage.All, GpuAccess.ShaderWrite | GpuAccess.CopyWrite);
        foreach (NativePassBuilder pass in Passes)
        {
            // Texture transitions already express both access and layout dependencies. A global
            // barrier cannot perform an access transition that itself requires a texture layout change.
            NativePassUsage next = pass.Uses.Where(use => !explicitLayouts || use.Key is NativePassBuffer)
                .Select(use => use.Value).Aggregate(new NativePassUsage(), static (value, use) => new(value.Stages | use.Stages, value.Access | use.Access));
            if (next.Stages != 0) { commands.Barrier(previous.Stages, previous.Access, next.Stages, next.Access); }
            foreach ((NativePassResource resource, NativePassUsage usage) in pass.Uses)
            {
                if (resource is NativePassTexture texture)
                {
                    GpuTextureLayout layout = explicitLayouts ? TextureLayout(usage.Access) : GpuTextureLayout.General;
                    NativeGpuTextureView view = WholeView(Services.Resources.GetTextureHandle((GpuTextureRef)texture.Reference), texture.Description);
                    if (!texture.Initialized) { commands.DiscardTexture(view, layout); texture.Initialized = true; }
                    else if (explicitLayouts) { commands.TextureTransition(view, texture.Layout, layout); }
                    texture.Layout = layout;
                }
            }
            pass.Record(context);
            if (next.Stages != 0) { previous = next; }
        }
        foreach (NativePassTexture texture in AllResources.OfType<NativePassTexture>())
        {
            if (texture.Initialized && texture.Layout != GpuTextureLayout.General)
            {
                commands.TextureTransition(WholeView(Services.Resources.GetTextureHandle((GpuTextureRef)texture.Reference), texture.Description), texture.Layout, GpuTextureLayout.General);
                texture.Layout = GpuTextureLayout.General;
            }
        }
        if (previous.Stages != 0) { commands.Barrier(previous.Stages, previous.Access, GpuStage.All, GpuAccess.ShaderRead | GpuAccess.CopyRead); }
    }
    internal static NativeGpuTextureView WholeView(NativeGpuTextureHandle texture, NativeGpuTextureDescription description)
        => new(texture, description.Dimension switch
        {
            NativeGpuTextureDimension.OneD => NativeGpuTextureViewDimension.OneD,
            NativeGpuTextureDimension.ThreeD => NativeGpuTextureViewDimension.ThreeD,
            _ => description.LayerCount == 1 ? NativeGpuTextureViewDimension.TwoD : NativeGpuTextureViewDimension.TwoDArray,
        }, description.Format, Aspect(description.Format), 0, description.MipCount, 0, description.LayerCount);
    internal static NativeGpuTextureAspect Aspect(GpuFormat format) => format switch
    {
        GpuFormat.D32Float => NativeGpuTextureAspect.Depth,
        GpuFormat.Depth24PlusStencil8 => NativeGpuTextureAspect.Depth | NativeGpuTextureAspect.Stencil,
        _ => NativeGpuTextureAspect.Color,
    };
    private static NativeGpuTextureUsage TextureUsage(GpuAccess access)
    {
        NativeGpuTextureUsage usage = 0;
        if ((access & GpuAccess.CopyRead) != 0) { usage |= NativeGpuTextureUsage.CopySource; }
        if ((access & GpuAccess.CopyWrite) != 0) { usage |= NativeGpuTextureUsage.CopyDestination; }
        if ((access & GpuAccess.ShaderRead) != 0) { usage |= NativeGpuTextureUsage.Sampled; }
        if ((access & GpuAccess.ShaderWrite) != 0) { usage |= NativeGpuTextureUsage.Storage; }
        if ((access & (GpuAccess.ColorRead | GpuAccess.ColorWrite)) != 0) { usage |= NativeGpuTextureUsage.ColorAttachment; }
        if ((access & (GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite)) != 0) { usage |= NativeGpuTextureUsage.DepthStencilAttachment; }
        return usage;
    }
    private static GpuTextureLayout TextureLayout(GpuAccess access)
    {
        if ((access & GpuAccess.ColorWrite) != 0) { return GpuTextureLayout.ColorAttachment; }
        if ((access & (GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite)) != 0) { return GpuTextureLayout.DepthStencilWrite; }
        if ((access & GpuAccess.CopyWrite) != 0) { return GpuTextureLayout.CopyDestination; }
        if ((access & GpuAccess.CopyRead) != 0) { return GpuTextureLayout.CopySource; }
        if ((access & GpuAccess.ShaderWrite) != 0) { return GpuTextureLayout.General; }
        return GpuTextureLayout.ShaderRead;
    }
}
