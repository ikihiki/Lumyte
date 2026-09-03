namespace Lumyte.Graphics.RenderGraph;

internal readonly record struct GpuRenderGraphResourceStructure(
    string Name,
    GpuRenderGraphResourceKind Kind,
    GpuMemoryKind MemoryKind,
    bool IsTransient,
    bool IsExported,
    bool IsOutput,
    GpuTextureDescription? TextureDescription,
    GpuBufferDescription? BufferDescription);
