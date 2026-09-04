namespace Lumyte.Graphics.RenderGraph;

/// <summary>Associates one bindless buffer-array index with one graph buffer read.</summary>
public readonly record struct GpuRenderGraphShaderBufferBinding(
    int Index,
    GpuRenderGraphBuffer Buffer,
    GpuStage Stages = GpuStage.PixelShader,
    BufferId Descriptor = default);
