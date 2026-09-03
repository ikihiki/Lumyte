namespace Lumyte.Graphics.RenderGraph;

/// <summary>A namespaced registration view over one frame's render graph.</summary>
public sealed class GpuRenderGraphContributionContext
{
    private readonly GpuRenderGraph graph;
    private readonly IReadOnlyDictionary<string, GpuRenderGraphResource> readableSharedResources;
    private readonly Dictionary<string, GpuRenderGraphResource> sharedResources;
    private bool open = true;

    internal GpuRenderGraphContributionContext(
        GpuRenderGraph graph,
        string name,
        Dictionary<string, GpuRenderGraphResource> sharedResources)
    {
        this.graph = graph;
        Name = name;
        this.sharedResources = sharedResources;
        readableSharedResources = sharedResources;
    }

    public string Name { get; }

    public GpuRenderGraphResource ImportTexture(string name, GpuTextureHandle texture)
        => graph.ImportTexture(Qualify(name), texture);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription description)
        => graph.ImportTexture(Qualify(name), texture, description);

    public GpuRenderGraphResource ImportTexture(
        string name,
        GpuRenderGraphExportedTexture texture)
        => graph.ImportTexture(Qualify(name), texture);

    public GpuRenderGraphResource ImportBuffer(string name, GpuBufferHandle buffer)
        => graph.ImportBuffer(Qualify(name), buffer);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
        => graph.ImportBuffer(Qualify(name), buffer, description);

    public GpuRenderGraphResource ImportBuffer(
        string name,
        GpuRenderGraphExportedBuffer buffer)
        => graph.ImportBuffer(Qualify(name), buffer);

    public GpuRenderGraphResource CreateTexture(
        string name,
        GpuTextureDescription description)
        => graph.CreateTexture(Qualify(name), description);

    public GpuRenderGraphResource CreateBuffer(
        string name,
        GpuBufferDescription description,
        GpuMemoryKind memoryKind = GpuMemoryKind.DeviceLocal)
        => graph.CreateBuffer(Qualify(name), description, memoryKind);

    public GpuRenderGraphPassBuilder AddPass<TState>(
        string name,
        TState state,
        GpuRenderGraphPassAction<TState> record,
        GpuRenderGraphPassFlags flags = GpuRenderGraphPassFlags.None)
        => graph.AddPass(Qualify(name), state, record, flags);

    public GpuRenderGraphContributionContext MarkOutput(GpuRenderGraphResource resource)
    {
        VerifyOpen();
        graph.MarkOutput(resource);
        return this;
    }

    public GpuRenderGraphResource ExportTexture(GpuRenderGraphResource resource)
    {
        VerifyOpen();
        return graph.ExportTexture(resource);
    }

    public GpuRenderGraphResource ExportBuffer(GpuRenderGraphResource resource)
    {
        VerifyOpen();
        return graph.ExportBuffer(resource);
    }

    /// <summary>Publishes a resource for contributors that execute later in registration order.</summary>
    public GpuRenderGraphResource PublishResource(
        string name,
        GpuRenderGraphResource resource)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        graph.RequireOwnedResource(resource);
        if (!sharedResources.TryAdd(name, resource))
        {
            throw new ArgumentException(
                $"A shared render-graph resource named '{name}' is already published.",
                nameof(name));
        }
        return resource;
    }

    public GpuRenderGraphResource GetResource(string name)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return readableSharedResources.TryGetValue(name, out GpuRenderGraphResource resource)
            ? resource
            : throw new KeyNotFoundException(
                $"Shared render-graph resource '{name}' has not been published by an earlier contributor.");
    }

    internal void Close() => open = false;

    private string Qualify(string localName)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        if (localName.Contains("::", StringComparison.Ordinal))
        {
            throw new ArgumentException("Local render-graph names cannot contain '::'.", nameof(localName));
        }
        return $"{Name}::{localName}";
    }

    private void VerifyOpen()
        => ObjectDisposedException.ThrowIf(!open, this);
}
