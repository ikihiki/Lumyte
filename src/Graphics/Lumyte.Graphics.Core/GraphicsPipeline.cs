namespace Lumyte.Graphics;

/// <summary>
/// Owns a graphics pipeline and its reflected material schema.
/// </summary>
public sealed class GraphicsPipeline : GpuResource
{
    internal GraphicsPipeline(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }

    /// <summary>
    /// Creates leased drawing arguments for a complete material range with the same shader schema.
    /// </summary>
    /// <param name="materials">The logical material snapshot, packed set, or opaque material range.</param>
    /// <returns>The owned arguments retaining the pipeline and its referenced resources.</returns>
    public ShaderArguments CreateArguments(MaterialBufferReference materials) => new(Driver, Driver.CreateMaterialArguments(Handle, materials));
}
