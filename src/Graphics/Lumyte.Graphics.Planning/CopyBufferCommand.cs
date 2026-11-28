using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class CopyBufferCommand<T>(PlanBuffer<T> source, PlanRange sourceRange, PlanBuffer<T> destination, PlanRange destinationRange) : PlanCommand
    where T : unmanaged
{
    private BufferSlice<T> _src;
    private BufferSlice<T> _dst;

    internal override void Prepare(PreparedResources resources, ArgumentTablePlan tables) => (_src, _dst) = (resources.Buffer(source, sourceRange), resources.Buffer(destination, destinationRange));

    internal override void Record(IGraphicsCommandBuffer commands) => commands.CopyBuffer(_src, _dst);
}
