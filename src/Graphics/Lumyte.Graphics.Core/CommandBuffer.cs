namespace Lumyte.Graphics;

/// <summary>
/// Owns finished commands that may be submitted once; disposal before submission discards them.
/// </summary>
public sealed class CommandBuffer : GpuResource
{
    internal CommandBuffer(IGraphicsDriver driver, object handle)
        : base(driver, handle)
    {
    }
}
