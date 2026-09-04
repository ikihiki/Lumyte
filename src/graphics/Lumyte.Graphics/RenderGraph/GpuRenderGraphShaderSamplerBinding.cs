namespace Lumyte.Graphics.RenderGraph;

/// <summary>Associates one bindless sampler-array index with a device-issued sampler.</summary>
public readonly record struct GpuRenderGraphShaderSamplerBinding(int Index, SamplerId Sampler);
