namespace Lumyte.Graphics;

/// <summary>
/// Owns a compute pipeline for the initial UInt32 storage schema.
/// </summary>
public sealed class ComputePipeline : GpuResource
{
    internal ComputePipeline(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }

    /// <summary>
    /// Creates leased compute arguments from a valid writable UInt32 storage range.
    /// </summary>
    /// <param name="data">The non-owning storage range to reference.</param>
    /// <returns>The owned arguments retaining the pipeline and its referenced resources.</returns>
    public ShaderArguments CreateArguments(IGpuRef<uint> data) => new(Driver, Driver.CreateArguments(Handle, data));
}
