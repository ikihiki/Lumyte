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
    /// Gets the fixed material ABI validated against the embedded Slang reflection; unsupported shaders are rejected.
    /// </summary>
    /// <returns>The validated fixed layout associated with this shader.</returns>
    public MaterialResourceLayout GetMaterialResourceLayout() => Driver.GetMaterialLayout(Handle);
}
