using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class PreparedTextureBarrier(TextureBarrierDesc description) : PreparedPlanBarrier
{
    internal override void Record(IGraphicsCommandBuffer commands) => commands.Barrier(description);
}
