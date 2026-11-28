using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal abstract class PlanCommand
{
    internal virtual IReadOnlyList<PlanResource> PhysicalResources => [];

    internal virtual ulong ArgumentBytes => 0;

    internal virtual int ReferenceCount => 0;

    internal abstract void Prepare(PreparedResources resources, ArgumentTablePlan tables);

    internal virtual void PrepareNative(IGraphicsCommandBuffer commands)
    {
    }

    internal abstract void Record(IGraphicsCommandBuffer commands);
}
