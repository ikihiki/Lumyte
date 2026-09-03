namespace Lumyte.Graphics.RenderGraph.Common;

public sealed class DrawMaterial
{
    private readonly DrawSampledTexture[] sampledTextures;

    public DrawMaterial(
        GpuRasterPipelineHandle pipeline,
        GpuResourceTable? resources = null,
        IEnumerable<DrawSampledTexture>? sampledTextures = null)
    {
        if (pipeline.IsNull) { throw new ArgumentException("Pipeline cannot be null.", nameof(pipeline)); }
        Pipeline = pipeline;
        Resources = resources;
        this.sampledTextures = sampledTextures?.ToArray() ?? [];
        foreach (DrawSampledTexture texture in this.sampledTextures) { texture.Validate(); }
        if ((resources is null && this.sampledTextures.Length != 0)
            || (resources is not null && resources.TextureSlotCount != this.sampledTextures.Length))
        {
            throw new ArgumentException(
                "Every material texture slot must have one declared sampled texture.",
                nameof(sampledTextures));
        }
    }

    public GpuRasterPipelineHandle Pipeline { get; }
    public GpuResourceTable? Resources { get; }

    /// <summary>
    /// Texture handles referenced by <see cref="Resources"/>. They are listed separately so the
    /// render graph can plan their read dependencies; ownership remains with the caller.
    /// </summary>
    public IReadOnlyList<DrawSampledTexture> SampledTextures => sampledTextures;
}
