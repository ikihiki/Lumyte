namespace Lumyte.Graphics.RenderGraph;

public sealed record GpuRenderGraphResourceInfo(
    GpuRenderGraphResource Resource,
    string Name,
    GpuRenderGraphResourceKind Kind,
    GpuTextureHandle Texture,
    GpuBufferHandle Buffer)
{
    public GpuTextureDescription? TextureDescription { get; internal init; }
    public GpuBufferDescription? BufferDescription { get; internal init; }
    public GpuMemoryKind MemoryKind { get; internal init; } = GpuMemoryKind.DeviceLocal;
    public bool IsTransient { get; internal init; }
    public bool IsExported { get; internal set; }

    internal GpuRenderGraphExportedTexture? ImportedTexture { get; init; }
    internal GpuRenderGraphExportedBuffer? ImportedBuffer { get; init; }
}
