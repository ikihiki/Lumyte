namespace Lumyte.Graphics.RenderGraph;

/// <summary>A namespaced registration view over one frame's render graph.</summary>
public sealed class GpuRenderGraphContributionContext
{
    private readonly GpuRenderGraph graph;
    private readonly Dictionary<string, GpuRenderGraphTexture> sharedTextures;
    private readonly Dictionary<string, GpuRenderGraphBuffer> sharedBuffers;
    private readonly Dictionary<string, GpuRenderGraphDependency> sharedDependencies;
    private readonly HashSet<string> sharedNames;
    private bool open = true;

    internal GpuRenderGraphContributionContext(
        GpuRenderGraph graph,
        string name,
        Dictionary<string, GpuRenderGraphTexture> sharedTextures,
        Dictionary<string, GpuRenderGraphBuffer> sharedBuffers,
        Dictionary<string, GpuRenderGraphDependency> sharedDependencies,
        HashSet<string> sharedNames)
    {
        this.graph = graph;
        Name = name;
        this.sharedTextures = sharedTextures;
        this.sharedBuffers = sharedBuffers;
        this.sharedDependencies = sharedDependencies;
        this.sharedNames = sharedNames;
    }

    public string Name { get; }

    public GpuRenderGraphTexture ImportTexture(
        string name,
        GpuTextureHandle texture,
        GpuTextureDescription description)
        => graph.ImportTexture(Qualify(name), texture, description);

    public GpuRenderGraphTexture ImportTexture(
        string name,
        GpuRenderGraphExportedTexture texture)
        => graph.ImportTexture(Qualify(name), texture);

    public GpuRenderGraphBuffer ImportBuffer(
        string name,
        GpuBufferHandle buffer,
        GpuBufferDescription description)
        => graph.ImportBuffer(Qualify(name), buffer, description);

    public GpuRenderGraphBuffer ImportBuffer(
        string name,
        GpuRenderGraphExportedBuffer buffer)
        => graph.ImportBuffer(Qualify(name), buffer);

    public GpuRenderGraphTexture CreateTexture(
        string name,
        GpuTextureDescription description)
        => graph.CreateTexture(Qualify(name), description);

    public GpuRenderGraphBuffer CreateBuffer(
        string name,
        GpuBufferDescription description,
        GpuMemoryKind memoryKind = GpuMemoryKind.DeviceLocal)
        => graph.CreateBuffer(Qualify(name), description, memoryKind);

    public GpuRenderGraphDependency CreateDependency(string name)
        => graph.CreateDependency(Qualify(name));

    public GpuRenderGraphPassBuilder AddPass<TState>(
        string name,
        TState state,
        GpuRenderGraphPassAction<TState> record,
        GpuRenderGraphPassFlags flags = GpuRenderGraphPassFlags.None)
        => graph.AddPass(Qualify(name), state, record, flags);

    public GpuRenderGraphContributionContext MarkOutput(GpuRenderGraphTexture texture)
    {
        VerifyOpen();
        graph.MarkOutput(texture);
        return this;
    }

    public GpuRenderGraphContributionContext MarkOutput(GpuRenderGraphBuffer buffer)
    {
        VerifyOpen();
        graph.MarkOutput(buffer);
        return this;
    }

    public GpuRenderGraphContributionContext MarkOutput(GpuRenderGraphDependency dependency)
    {
        VerifyOpen();
        graph.MarkOutput(dependency);
        return this;
    }

    public GpuRenderGraphTexture ExportTexture(GpuRenderGraphTexture texture)
    {
        VerifyOpen();
        return graph.ExportTexture(texture);
    }

    public GpuRenderGraphBuffer ExportBuffer(GpuRenderGraphBuffer buffer)
    {
        VerifyOpen();
        return graph.ExportBuffer(buffer);
    }

    /// <summary>Publishes a texture for contributors that execute later in registration order.</summary>
    public GpuRenderGraphTexture PublishTexture(string name, GpuRenderGraphTexture texture)
    {
        VerifyOpen();
        ValidateSharedName(name);
        graph.RequireOwnedTexture(texture);
        sharedTextures.Add(name, texture);
        return texture;
    }

    /// <summary>Publishes a buffer for contributors that execute later in registration order.</summary>
    public GpuRenderGraphBuffer PublishBuffer(string name, GpuRenderGraphBuffer buffer)
    {
        VerifyOpen();
        ValidateSharedName(name);
        graph.RequireOwnedBuffer(buffer);
        sharedBuffers.Add(name, buffer);
        return buffer;
    }

    /// <summary>Publishes an ordering dependency for contributors registered later.</summary>
    public GpuRenderGraphDependency PublishDependency(
        string name,
        GpuRenderGraphDependency dependency)
    {
        VerifyOpen();
        ValidateSharedName(name);
        graph.RequireOwnedDependency(dependency);
        sharedDependencies.Add(name, dependency);
        return dependency;
    }

    public GpuRenderGraphTexture GetTexture(string name)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return sharedTextures.TryGetValue(name, out GpuRenderGraphTexture texture)
            ? texture
            : throw MissingSharedValue(name, "texture");
    }

    public GpuRenderGraphBuffer GetBuffer(string name)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return sharedBuffers.TryGetValue(name, out GpuRenderGraphBuffer buffer)
            ? buffer
            : throw MissingSharedValue(name, "buffer");
    }

    public GpuRenderGraphDependency GetDependency(string name)
    {
        VerifyOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return sharedDependencies.TryGetValue(name, out GpuRenderGraphDependency dependency)
            ? dependency
            : throw MissingSharedValue(name, "dependency");
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

    private void ValidateSharedName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!sharedNames.Add(name))
        {
            throw new ArgumentException(
                $"A shared render-graph value named '{name}' is already published.",
                nameof(name));
        }
    }

    private static KeyNotFoundException MissingSharedValue(string name, string kind)
        => new($"Shared render-graph {kind} '{name}' has not been published by an earlier contributor.");

    private void VerifyOpen()
        => ObjectDisposedException.ThrowIf(!open, this);
}
