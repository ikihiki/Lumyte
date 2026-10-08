namespace Lumyte.Graphics;

/// <summary>
/// Owns a graphics pipeline and its reflected shader-data schema.
/// </summary>
public sealed class GraphicsPipeline : GpuResource
{
    internal GraphicsPipeline(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }

    /// <summary>Automatically collects dependencies from a shader-data root range and constructs drawing bindings.</summary>
    /// <typeparam name="T">The caller-owned logical element type.</typeparam>
    /// <param name="data">One logical element or the shader's candidate array range.</param>
    /// <returns>The owned drawing arguments and their retained automatic bindings.</returns>
    public ShaderArguments CreateArguments<T>(IGpuRef<T> data) => new(Driver, Driver.CreateDrawingArguments(Handle, data));
}
