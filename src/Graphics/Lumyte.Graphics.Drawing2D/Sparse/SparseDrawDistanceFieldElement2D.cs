using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class SparseDrawDistanceFieldElement2D(SparseCpuService2D service) : PipelineElement2D, IDrawDistanceFieldRenderer2D, IPipelineAdmission2D<DrawDistanceFieldCommand2D>
{
    internal SparseCpuService2D Service => service;

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands)
        => service.CanPrepare(context);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default)
        => service.PrepareAsync(context, commands, cancellationToken);
}
