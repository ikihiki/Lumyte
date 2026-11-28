using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class PreparedShaderDataBarrier<T>(ShaderDataBufferBarrierDesc<T> description) : PreparedPlanBarrier
    where T : struct, IShaderArguments
{
    internal override void Record(IGraphicsCommandBuffer commands) => commands.Barrier(description);
}
