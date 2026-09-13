namespace Lumyte.Graphics.RenderGraph;

public enum GpuGraphTextureDimension { OneD, TwoD, ThreeD }

public sealed record GpuGraphTextureDescription(
    uint Width, uint Height, GpuFormat Format, uint DepthOrArrayLayers = 1,
    uint MipLevelCount = 1, uint SampleCount = 1,
    GpuGraphTextureDimension Dimension = GpuGraphTextureDimension.TwoD);

public sealed record GpuGraphBufferDescription(ulong Size);

/// <summary>A CPU declaration, never a physical GPU handle.</summary>
public abstract class GpuRenderGraphResource
{
    private protected GpuRenderGraphResource(GpuRenderGraph graph, string name,
        GpuGraphResourceRef? importedReference = null, bool isInput = false)
    {
        Graph = graph;
        Name = name;
        ImportedReference = importedReference;
        IsInput = isInput;
    }

    internal GpuRenderGraph Graph { get; }
    public string Name { get; }
    public GpuGraphResourceRef? ImportedReference { get; }
    public bool IsInput { get; }
}

public sealed class GpuRenderGraphTexture : GpuRenderGraphResource
{
    internal GpuRenderGraphTexture(GpuRenderGraph graph, string name, GpuGraphTextureDescription description,
        GpuGraphTextureRef? importedReference = null, bool isInput = false)
        : base(graph, name, importedReference, isInput) => Description = description;

    public GpuGraphTextureDescription Description { get; }
}

public sealed class GpuRenderGraphBuffer : GpuRenderGraphResource
{
    internal GpuRenderGraphBuffer(GpuRenderGraph graph, string name, GpuGraphBufferDescription description,
        GpuGraphBufferRef? importedReference = null, bool isInput = false)
        : base(graph, name, importedReference, isInput) => Description = description;

    public GpuGraphBufferDescription Description { get; }
}

public sealed class GpuRenderGraphDependency : GpuRenderGraphResource
{
    internal GpuRenderGraphDependency(GpuRenderGraph graph, string name) : base(graph, name) { }
}

public sealed class GpuGraphTextureInput
{
    internal GpuGraphTextureInput(GpuRenderGraphTexture texture) => Texture = texture;
    public GpuRenderGraphTexture Texture { get; }
}

public sealed class GpuGraphBufferInput
{
    internal GpuGraphBufferInput(GpuRenderGraphBuffer buffer) => Buffer = buffer;
    public GpuRenderGraphBuffer Buffer { get; }
}
