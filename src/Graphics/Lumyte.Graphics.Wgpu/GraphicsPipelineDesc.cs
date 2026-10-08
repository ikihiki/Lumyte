namespace Lumyte.Graphics.Wgpu;

internal sealed record GraphicsPipelineDesc
{
    public required ShaderModule Shader { get; init; }

    public string VertexEntry { get; init; } = "vertexMain";

    public string FragmentEntry { get; init; } = "fragmentMain";
}
