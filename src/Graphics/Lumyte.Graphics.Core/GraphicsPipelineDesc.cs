namespace Lumyte.Graphics;
/// <summary>
/// Describes a triangle-list pipeline with one RGBA8Unorm output.
/// </summary>
public sealed record GraphicsPipelineDesc
{
    /// <summary>
    /// Gets the shader module that contains both graphics entry points.
    /// </summary>
    public required ShaderModule Shader { get; init; }

    /// <summary>
    /// Gets the vertex entry point name; defaults to vertexMain.
    /// </summary>
    public string VertexEntry { get; init; } = "vertexMain";

    /// <summary>
    /// Gets the fragment entry point name; defaults to fragmentMain.
    /// </summary>
    public string FragmentEntry { get; init; } = "fragmentMain";
}
