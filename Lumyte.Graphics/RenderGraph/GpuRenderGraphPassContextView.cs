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

    public GpuTextureHandle GetTexture(GpuRenderGraphResource resource)
        => RequireResource(resource, GpuRenderGraphResourceKind.Texture).Texture;

    public GpuBufferHandle GetBuffer(GpuRenderGraphResource resource)
        => RequireResource(resource, GpuRenderGraphResourceKind.Buffer).Buffer;

    public GpuTextureView GetTextureView(GpuRenderGraphResource resource)
    {
        GpuRenderGraphResourceRuntime runtime = RequireResource(
            resource,
            GpuRenderGraphResourceKind.Texture);
        if (runtime.View is { } view) { return view; }
        if (backend is null)
        {
            throw new InvalidOperationException(
                "Texture views for graph resources require Execute(IGpuBackend).");
        }
        GpuTextureDescription description = runtime.Info.TextureDescription
            ?? throw new InvalidOperationException(
                "The imported texture has no description for view creation.");
        view = backend.CreateTextureView(runtime.Texture, new(description.Format));
        runtime.View = view;
        return view;
    }

    public GpuMemoryAddress GetBufferMemoryAddress(
        GpuRenderGraphResource resource,
        ulong offset = 0,
        ulong length = 0)
    {
        GpuRenderGraphResourceRuntime runtime = RequireResource(
            resource,
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
