namespace Lumyte.Graphics.Library;

/// <summary>A shader-data buffer mapped to one bindless buffer-array index.</summary>
public readonly record struct DrawShaderBuffer(
    int Index,
    GpuBufferHandle Buffer,
    GpuBufferDescription Description,
    GpuStage Stages = GpuStage.PixelShader)
{
    public DrawShaderBuffer Validate()
    {
        if (Index < 0) { throw new ArgumentOutOfRangeException(nameof(Index)); }
        if (Buffer.IsNull) { throw new ArgumentException("Shader buffer cannot be null.", nameof(Buffer)); }
        Description.Validate();
        if (Buffer.Size != Description.Size)
        {
            throw new ArgumentException("Shader buffer size does not match its description.", nameof(Buffer));
        }
        if ((Description.Usage & GpuBufferUsage.ShaderData) == 0)
        {
            throw new ArgumentException("Shader buffer description requires ShaderData usage.", nameof(Description));
        }
        const GpuStage supportedStages = GpuStage.VertexShader | GpuStage.PixelShader;
        if (Stages == GpuStage.None || (Stages & ~supportedStages) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Stages));
        }
        return this;
    }
}
