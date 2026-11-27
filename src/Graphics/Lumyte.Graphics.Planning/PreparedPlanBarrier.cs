using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal abstract class PreparedPlanBarrier
{
    internal abstract void Record(IGraphicsCommandBuffer commands);
}
