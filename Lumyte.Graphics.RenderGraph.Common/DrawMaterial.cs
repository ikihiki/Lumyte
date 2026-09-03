namespace Lumyte.Graphics.RenderGraph.Common;

public sealed class DrawMaterial
{
    private readonly DrawSampledTexture[] sampledTextures;
    private readonly DrawShaderBuffer[] shaderBuffers;

    public DrawMaterial(
        GpuRasterPipelineHandle pipeline,
        GpuResourceTable? resources = null,
        IEnumerable<DrawSampledTexture>? sampledTextures = null,
        IEnumerable<DrawShaderBuffer>? shaderBuffers = null)
    {
        if (pipeline.IsNull) { throw new ArgumentException("Pipeline cannot be null.", nameof(pipeline)); }
        Pipeline = pipeline;
        Resources = resources;
        this.sampledTextures = sampledTextures?.ToArray() ?? [];
        this.shaderBuffers = shaderBuffers?.OrderBy(static buffer => buffer.Index).ToArray() ?? [];
        foreach (DrawSampledTexture texture in this.sampledTextures) { texture.Validate(); }
        foreach (DrawShaderBuffer buffer in this.shaderBuffers) { buffer.Validate(); }
        if (resources is null && this.sampledTextures.Length != 0
            || resources is not null && resources.TextureSlotCount != this.sampledTextures.Length)
        {
            throw new ArgumentException(
                "Every material texture descriptor index must have one declared sampled texture.",
                nameof(sampledTextures));
        }
        if (resources is not null)
        {
            for (int index = 0; index < resources.TextureSlotCount; index++)
            {
                if (resources.GetTexture(index).IsNull)
                {
                    throw new ArgumentException(
                        "Material texture descriptor indices cannot be empty.",
                        nameof(resources));
                }
            }
        }
        if (resources is null && this.shaderBuffers.Length != 0)
        {
            throw new ArgumentException(
                "Shader buffers require a material resource table.",
                nameof(shaderBuffers));
        }
        if (resources is not null)
        {
            ValidateShaderBuffers(resources, this.shaderBuffers);
        }
    }

    public GpuRasterPipelineHandle Pipeline { get; }
    public GpuResourceTable? Resources { get; }

    /// <summary>
    /// Texture handles referenced by <see cref="Resources"/>. They are listed separately so the
    /// render graph can plan their read dependencies; ownership remains with the caller.
    /// </summary>
    public IReadOnlyList<DrawSampledTexture> SampledTextures => sampledTextures;
    public IReadOnlyList<DrawShaderBuffer> ShaderBuffers => shaderBuffers;

    private static void ValidateShaderBuffers(
        GpuResourceTable resources,
        DrawShaderBuffer[] shaderBuffers)
    {
        int occupiedCount = 0;
        for (int index = 0; index < resources.BufferSlotCount; index++)
        {
            if (!resources.GetBuffer(index).IsNull) { occupiedCount++; }
        }
        if (occupiedCount != shaderBuffers.Length)
        {
            throw new ArgumentException(
                "Every occupied material buffer index must have one declared shader buffer.",
                nameof(shaderBuffers));
        }
        for (int index = 0; index < shaderBuffers.Length; index++)
        {
            DrawShaderBuffer buffer = shaderBuffers[index];
            if (buffer.Index >= resources.BufferSlotCount
                || resources.GetBuffer(buffer.Index).IsNull
                || (index != 0 && shaderBuffers[index - 1].Index == buffer.Index))
            {
                throw new ArgumentException(
                    "Shader buffer indices must be unique and match the material resource table.",
                    nameof(shaderBuffers));
            }
        }
    }
}
