namespace Lumyte.Graphics;

/// <summary>
/// Owns a shader loaded from embedded resources.
/// </summary>
public sealed class ShaderModule : GpuResource
{
    internal ShaderModule(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }

    /// <summary>
    /// Gets the caller-defined element layout validated against embedded Slang reflection; unsupported schemas are rejected.
    /// </summary>
    /// <returns>The reflected element layout and compiled sampled-resource capacity.</returns>
    public MaterialResourceLayout GetMaterialResourceLayout() => Driver.GetMaterialLayout(Handle);
}
