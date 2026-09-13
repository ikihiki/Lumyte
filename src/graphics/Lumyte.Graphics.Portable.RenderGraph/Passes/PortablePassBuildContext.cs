using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.RenderGraph;

public delegate void PortablePassRecordAction<TState>(PortablePassRecordContext context, TState state);

/// <summary>One feature build. Resource allocation and recording occur after every feature has declared its uses.</summary>
public sealed class PortablePassBuildContext
{
    private readonly PortableExecutionBuild build;
    private readonly GpuRenderGraphPass pass;
    private bool ended;
    internal PortablePassBuildContext(PortableExecutionBuild build, GpuRenderGraphPass pass) { this.build = build; this.pass = pass; }
    public PortablePassServices Services => build.Services;
    private void Check() { ObjectDisposedException.ThrowIf(ended, this); }
    internal void End() { ended = true; }
    public T GetInput<T>(GpuGraphValue<T> value) { Check(); return pass.GetInput(value, build.Bindings); }
    public PortablePassTexture ImportTexture(GpuRenderGraphTexture texture)
    { Check(); RequireDeclared(texture); return (PortablePassTexture)build.Resources[texture]; }
    public PortablePassBuffer ImportBuffer(GpuRenderGraphBuffer buffer)
    { Check(); RequireDeclared(buffer); return (PortablePassBuffer)build.Resources[buffer]; }
    private void RequireDeclared(GpuRenderGraphResource resource)
    {
        if (!pass.Uses.Any(use => ReferenceEquals(use.Resource, resource)))
        { throw new ArgumentException("The feature contract did not declare this resource.", nameof(resource)); }
    }
    public PortablePassTexture ImportTexture(GpuTextureRef reference)
    { Check(); return build.ImportTexture(reference); }
    public PortablePassBuffer ImportBuffer(GpuBufferRef reference)
    { Check(); return build.ImportBuffer(reference); }
    public PortablePassTexture CreateTexture(string name, GpuTextureDescription description)
    { Check(); var resource = new PortablePassTexture(build, name, description); build.InternalResources.Add(resource); return resource; }
    public PortablePassBuffer CreateBuffer(string name, GpuBufferDescription description)
    { Check(); var resource = new PortablePassBuffer(build, name, description); build.InternalResources.Add(resource); return resource; }
    public PortablePassView CreateView(string name, PortablePassTexture texture, GpuTextureViewDescription description = default)
    {
        Check(); build.Check(texture); ArgumentException.ThrowIfNullOrEmpty(name);
        var view = new PortablePassView(texture, description); build.Views.Add(view); return view;
    }
    public PortablePassBindings CreateBindings(string name, PortableShaderProgram program, uint group, IPortablePassBindingInputs inputs)
    {
        Check(); ArgumentException.ThrowIfNullOrEmpty(name); ArgumentNullException.ThrowIfNull(program); ArgumentNullException.ThrowIfNull(inputs);
        var writer = new PortablePassBindingWriter(build); inputs.Write(writer);
        var result = new PortablePassBindings(build, program, group, writer.Finish()); build.BindingSets.Add(result); return result;
    }
    public PortablePassBuilder AddPass<TState>(string name, TState state, PortablePassRecordAction<TState> record)
    {
        Check(); ArgumentException.ThrowIfNullOrEmpty(name); ArgumentNullException.ThrowIfNull(record);
        var node = new PortablePassBuilder(build, context => record(context, state)); build.Passes.Add(node); return node;
    }
    public void Retain(IDisposable lease) { Check(); build.Batch.Retain(lease); }
}

/// <summary>Internal use declaration. Registration order respects the common feature order.</summary>
public sealed class PortablePassBuilder
{
    private readonly PortableExecutionBuild build;
    internal Action<PortablePassRecordContext> Record { get; }
    internal PortablePassBuilder(PortableExecutionBuild build, Action<PortablePassRecordContext> record) { this.build = build; Record = record; }
    public PortablePassBuilder Read(PortablePassResource resource, PortablePassUsage usage) => Use(resource, usage);
    public PortablePassBuilder Write(PortablePassResource resource, PortablePassUsage usage) => Use(resource, usage);
    public PortablePassBuilder ReadWrite(PortablePassResource resource, PortablePassUsage usage) => Use(resource, usage);
    private PortablePassBuilder Use(PortablePassResource resource, PortablePassUsage usage)
    {
        build.Check(resource);
        if (resource is PortablePassTexture texture)
        {
            GpuTextureUsage actual = usage switch
            {
                PortablePassUsage.SampledRead => GpuTextureUsage.Sampled,
                PortablePassUsage.StorageRead or PortablePassUsage.StorageWrite => GpuTextureUsage.Storage,
                PortablePassUsage.ColorAttachment => GpuTextureUsage.ColorAttachment,
                PortablePassUsage.DepthStencilAttachment => GpuTextureUsage.DepthStencilAttachment,
                PortablePassUsage.CopySource => GpuTextureUsage.CopySource,
                PortablePassUsage.CopyDestination => GpuTextureUsage.CopyDestination,
                _ => throw new ArgumentException("The usage describes a buffer, not a texture.", nameof(usage)),
            };
            if (texture.Reference is null) { texture.Description = texture.Description with { Usage = texture.Description.Usage | actual }; }
        }
        else if (resource is PortablePassBuffer buffer)
        {
            GpuBufferUsage actual = usage switch
            {
                PortablePassUsage.UniformRead => GpuBufferUsage.Uniform,
                PortablePassUsage.StorageRead or PortablePassUsage.StorageWrite => GpuBufferUsage.Storage,
                PortablePassUsage.CopySource => GpuBufferUsage.CopySource,
                PortablePassUsage.CopyDestination => GpuBufferUsage.CopyDestination,
                PortablePassUsage.IndexRead => GpuBufferUsage.Index,
                PortablePassUsage.IndirectRead => GpuBufferUsage.IndirectArguments,
                _ => throw new ArgumentException("The usage describes a texture, not a buffer.", nameof(usage)),
            };
            if (buffer.Reference is null) { buffer.Description = buffer.Description with { Usage = buffer.Description.Usage | actual }; }
        }
        return this;
    }
}

/// <summary>Prepared non-owning GPU objects, borrowed only for synchronous recording.</summary>
public sealed class PortablePassRecordContext
{
    private readonly PortableExecutionBuild build;
    internal PortablePassRecordContext(PortableExecutionBuild build, GpuCommandBuffer commands) { this.build = build; Commands = commands; }
    public GpuCommandBuffer Commands { get; }
    public GpuTextureHandle GetTexture(PortablePassTexture texture) { build.Check(texture); return build.Services.Resources.GetTextureHandle(texture.Reference!); }
    public GpuBufferRange GetBufferRange(PortablePassBuffer buffer, ulong offset = 0, ulong? length = null)
    { build.Check(buffer); return build.Services.Resources.GetBufferRange(buffer.Reference!, offset, length); }
    public GpuTextureView GetTextureView(PortablePassView view)
    { build.Check(view.Texture); return build.Services.Resources.GetTextureView(view.Reference!); }
    public GpuBindingsHandle GetBindings(PortablePassBindings bindings)
    {
        if (!ReferenceEquals(bindings.Owner, build)) { throw new ArgumentException("The bindings belong to another execution.", nameof(bindings)); }
        return build.Services.Resources.GetBindingsHandle(bindings.Reference!);
    }
}

internal sealed class PortableExecutionBuild(PortablePassServices services, GpuRenderGraphBindings bindings, GpuResourceBatch batch)
{
    internal PortablePassServices Services { get; } = services;
    internal GpuRenderGraphBindings Bindings { get; } = bindings;
    internal GpuResourceBatch Batch { get; } = batch;
    internal Dictionary<GpuRenderGraphResource, PortablePassResource> Resources { get; } = [];
    internal List<PortablePassResource> InternalResources { get; } = [];
    internal List<PortablePassView> Views { get; } = [];
    internal List<PortablePassBindings> BindingSets { get; } = [];
    internal List<PortablePassBuilder> Passes { get; } = [];
    internal void Check(PortablePassResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!ReferenceEquals(resource.Owner, this)) { throw new ArgumentException("The resource belongs to another execution.", nameof(resource)); }
    }
    internal PortablePassTexture ImportTexture(GpuTextureRef reference)
    {
        Batch.Use(reference);
        PortablePassTexture? existing = Resources.Values.Concat(InternalResources).OfType<PortablePassTexture>().FirstOrDefault(item => ReferenceEquals(item.Reference, reference));
        if (existing is not null) { return existing; }
        var resource = new PortablePassTexture(this, "Imported texture", reference.Description, reference); InternalResources.Add(resource); return resource;
    }
    internal PortablePassBuffer ImportBuffer(GpuBufferRef reference)
    {
        Batch.Use(reference);
        PortablePassBuffer? existing = Resources.Values.Concat(InternalResources).OfType<PortablePassBuffer>().FirstOrDefault(item => ReferenceEquals(item.Reference, reference));
        if (existing is not null) { return existing; }
        var resource = new PortablePassBuffer(this, "Imported buffer", reference.Description, reference); InternalResources.Add(resource); return resource;
    }
    internal void Prepare(GpuResourceScope scope)
    {
        foreach (PortablePassResource resource in Resources.Values.Concat(InternalResources).Distinct())
        {
            switch (resource)
            {
                case PortablePassTexture texture:
                    texture.Reference ??= scope.CreateTexture(texture.Description); Batch.Use(texture.Reference); break;
                case PortablePassBuffer buffer:
                    buffer.Reference ??= scope.CreateBuffer(buffer.Description); Batch.Use(buffer.Reference); break;
            }
        }
        foreach (PortablePassView view in Views) { view.Reference = scope.GetView(view.Texture.Reference!, view.Description); }
        foreach (PortablePassBindings binding in BindingSets)
        { binding.Reference = scope.GetBindings(binding.Program, binding.Group, new PreparedInputs(binding.Entries)); }
        Batch.Use(scope);
    }
    private sealed class PreparedInputs(IReadOnlyList<Action<GpuBindingWriter>> entries) : IGpuBindingInputs
    { public void Write(GpuBindingWriter writer) { foreach (Action<GpuBindingWriter> entry in entries) { entry(writer); } } }
}
