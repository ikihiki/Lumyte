using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Library;

public readonly record struct ComputeBufferBinding(
    uint Slot,
    GpuRenderGraphBuffer Buffer,
    GpuRenderGraphAccess Access)
{
    public ComputeBufferBinding Validate()
    {
        if (Buffer.IsNull) { throw new ArgumentException("Buffer cannot be null.", nameof(Buffer)); }
        if ((Buffer.Description.Usage & GpuBufferUsage.ShaderData) == 0)
        {
            throw new ArgumentException("Compute buffers require ShaderData usage.", nameof(Buffer));
        }
        if (Access is not GpuRenderGraphAccess.Read
            and not GpuRenderGraphAccess.Write
            and not GpuRenderGraphAccess.ReadWrite)
        {
            throw new ArgumentOutOfRangeException(nameof(Access));
        }
        return this;
    }
}
