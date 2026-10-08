namespace Lumyte.Graphics;
/// <summary>
/// Describes a compute pipeline for the initial storage-buffer schema.
/// </summary>
public sealed record ComputePipelineDesc
{
    /// <summary>
    /// Gets the shader module containing the compute entry point.
    /// </summary>
    public required ShaderModule Shader { get; init; }

    /// <summary>
    /// Gets the compute entry point name; defaults to computeMain.
    /// </summary>
    public string EntryPoint { get; init; } = "computeMain";
}
