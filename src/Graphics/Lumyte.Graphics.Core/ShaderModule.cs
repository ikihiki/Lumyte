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

    /// <summary>Gets a caller-owned logical type's wire schema from embedded Slang reflection.</summary>
    /// <typeparam name="T">The caller-owned logical element type.</typeparam>
    /// <returns>The reflected layout used by the explicit serializer.</returns>
    public ShaderDataLayout<T> GetDataLayout<T>() => Driver.GetDataLayout<T>(Handle);
}
