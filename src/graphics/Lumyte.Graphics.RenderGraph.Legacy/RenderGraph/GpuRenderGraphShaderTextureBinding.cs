namespace Lumyte.Graphics.RenderGraph.Legacy;

/// <summary>Associates one bindless texture-array index with one graph texture read.</summary>
public readonly record struct GpuRenderGraphShaderTextureBinding(
    int Index,
    GpuRenderGraphTexture Texture,
    GpuStage Stages = GpuStage.PixelShader,
    TextureId Descriptor = default);
