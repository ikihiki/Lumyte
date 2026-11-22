using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Pipelines;

internal abstract class PipelineExternalResource(IGraphicDevice device, GraphicsDeviceGeneration generation, object allocation, BarrierScope scope)
{
    private readonly object _gate = new();

    internal IGraphicDevice Device { get; } = device;

    internal GraphicsDeviceGeneration Generation { get; } = generation;

    internal object Allocation { get; } = allocation;

    internal long Order { get; init; }

    internal BarrierScope Scope { get; private protected set; } = scope;

    internal void AddAccess(BarrierScope scope)
    {
        lock (_gate)
        {
            Scope = new BarrierScope(Scope.Stages | scope.Stages, Scope.Access | scope.Access);
        }
    }

    internal abstract PipelineExternalResource Snapshot();
}
