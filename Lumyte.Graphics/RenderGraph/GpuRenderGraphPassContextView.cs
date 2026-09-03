namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// Stack-only pass context used by render-graph callbacks without allocating a context object.
/// </summary>
public readonly ref struct GpuRenderGraphPassContextView
{
    private readonly IGpuBackend? backend;
    private readonly IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources;
    private readonly IReadOnlySet<GpuRenderGraphResource> allowedResources;

    internal GpuRenderGraphPassContextView(
        GpuCommandBuffer commands,
        IGpuBackend? backend,
        IReadOnlyDictionary<GpuRenderGraphResource, GpuRenderGraphResourceRuntime> resources,
        IReadOnlySet<GpuRenderGraphResource> allowedResources)
    {
        Commands = commands;
        this.backend = backend;
        this.resources = resources;
        this.allowedResources = allowedResources;
    }

    public GpuCommandBuffer Commands { get; }

    public GpuTextureHandle GetTexture(GpuRenderGraphTexture texture)
        => RequireResource(texture.Resource, GpuRenderGraphResourceKind.Texture).Texture;

    public GpuBufferHandle GetBuffer(GpuRenderGraphBuffer buffer)
        => RequireResource(buffer.Resource, GpuRenderGraphResourceKind.Buffer).Buffer;

    public GpuTextureView GetTextureView(GpuRenderGraphTexture texture)
    {
        GpuRenderGraphResourceRuntime runtime = RequireResource(
            texture.Resource,
            GpuRenderGraphResourceKind.Texture);
        if (runtime.View is { } view) { return view; }
        if (backend is null)
        {
            throw new InvalidOperationException(
                "Texture views for graph resources require Execute(IGpuBackend).");
        }
        view = backend.CreateTextureView(runtime.Texture, new(texture.Description.Format));
        runtime.View = view;
        return view;
    }

    public GpuMemoryAddress GetBufferMemoryAddress(
        GpuRenderGraphBuffer buffer,
        ulong offset = 0,
        ulong length = 0)
    {
        GpuRenderGraphResourceRuntime runtime = RequireResource(
            buffer.Resource,
            GpuRenderGraphResourceKind.Buffer);
        if (backend is null)
        {
            throw new InvalidOperationException(
                "Buffer addresses for graph resources require Execute(IGpuBackend).");
        }
        if (offset > runtime.Buffer.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        ulong resolvedLength = length == 0 ? runtime.Buffer.Size - offset : length;
        if (resolvedLength > runtime.Buffer.Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        return backend.GetBufferMemoryAddress(runtime.Buffer, offset, resolvedLength);
    }

    private GpuRenderGraphResourceRuntime RequireResource(
        GpuRenderGraphResource resource,
        GpuRenderGraphResourceKind kind)
    {
        if (!allowedResources.Contains(resource))
        {
            throw new InvalidOperationException(
                "A pass may only resolve resources declared in its access list.");
        }
        if (!resources.TryGetValue(resource, out GpuRenderGraphResourceRuntime? runtime))
        {
            throw new ArgumentException(
                "Resource is not available in this graph execution.",
                nameof(resource));
        }
        if (runtime.Info.Kind != kind)
        {
            throw new ArgumentException(
                $"Resource is not a {kind.ToString().ToLowerInvariant()}.",
                nameof(resource));
        }
        return runtime;
    }
}
