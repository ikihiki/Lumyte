namespace Lumyte.Graphics;

/// <summary>
/// Retains the resources and backend bindings used by a pipeline invocation.
/// </summary>
public sealed class ShaderArguments : GpuResource
{
    internal ShaderArguments(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }
}
