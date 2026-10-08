namespace Lumyte.Graphics.Wgpu;

internal sealed record ComputePipelineDesc
{
    public required ShaderModule Shader { get; init; }

    public string EntryPoint { get; init; } = "computeMain";
}
