using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class PreparedBufferBarrier<T>(BufferBarrierDesc<T> description) : PreparedPlanBarrier
    where T : unmanaged
{
    internal override void Record(IGraphicsCommandBuffer commands) => commands.Barrier(description);
}
