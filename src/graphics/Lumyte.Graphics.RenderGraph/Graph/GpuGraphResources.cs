namespace Lumyte.Graphics.RenderGraph;

public enum GpuGraphTextureDimension { OneD, TwoD, ThreeD }

public sealed record GpuGraphTextureDescription(
    uint Width, uint Height, GpuFormat Format, uint DepthOrArrayLayers = 1,
    uint MipLevelCount = 1, uint SampleCount = 1,
    GpuGraphTextureDimension Dimension = GpuGraphTextureDimension.TwoD);

public sealed record GpuGraphBufferDescription(ulong Size);

// Declarations retain membership, without retaining the mutable builder and its culled content.
internal sealed class GpuRenderGraphIdentity;

/// <summary>A CPU declaration, never a physical GPU handle.</summary>
public abstract class GpuRenderGraphResource
{
    private protected GpuRenderGraphResource(GpuRenderGraphIdentity graphIdentity, string name,
        GpuGraphResourceRef? importedReference = null, bool isInput = false)
    {
        GraphIdentity = graphIdentity;
        Name = name;
        ImportedReference = importedReference;
        IsInput = isInput;
    }

    internal GpuRenderGraphIdentity GraphIdentity { get; }
    public string Name { get; }
    public GpuGraphResourceRef? ImportedReference { get; }
    public bool IsInput { get; }
}

public sealed class GpuRenderGraphTexture : GpuRenderGraphResource
{
    internal GpuRenderGraphTexture(GpuRenderGraphIdentity graphIdentity, string name, GpuGraphTextureDescription description,
        GpuGraphTextureRef? importedReference = null, bool isInput = false)
        : base(graphIdentity, name, importedReference, isInput) => Description = description;

    public GpuGraphTextureDescription Description { get; }
}

public sealed class GpuRenderGraphBuffer : GpuRenderGraphResource
{
    internal GpuRenderGraphBuffer(GpuRenderGraphIdentity graphIdentity, string name, GpuGraphBufferDescription description,
        GpuGraphBufferRef? importedReference = null, bool isInput = false)
        : base(graphIdentity, name, importedReference, isInput) => Description = description;

    public GpuGraphBufferDescription Description { get; }
}

public sealed class GpuRenderGraphDependency : GpuRenderGraphResource
{
    internal GpuRenderGraphDependency(GpuRenderGraphIdentity graphIdentity, string name) : base(graphIdentity, name) { }
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
