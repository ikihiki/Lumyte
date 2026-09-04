namespace Lumyte.Graphics.Library;

public readonly record struct DrawSampledTexture(
    GpuTextureHandle Texture,
    GpuTextureDescription Description,
    GpuStage Stages = GpuStage.PixelShader)
{
    public DrawSampledTexture Validate()
    {
        if (Texture.IsNull) { throw new ArgumentException("Sampled texture cannot be null.", nameof(Texture)); }
        Description.Validate();
        if ((Description.Usage & GpuTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("Sampled texture description requires Sampled usage.", nameof(Description));
        }
        const GpuStage supportedStages = GpuStage.VertexShader | GpuStage.PixelShader;
        if (Stages == GpuStage.None || (Stages & ~supportedStages) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Stages));
        }
        return this;
    }
}
