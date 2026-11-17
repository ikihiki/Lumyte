using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class SparseDrawImageElement2D(SparseCpuService2D service) : PipelineElement2D, IDrawImageRenderer2D, IPipelineAdmission2D<DrawImageCommand2D>
{
    internal SparseCpuService2D Service => service;

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands)
        => service.CanPrepare(context);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default)
        => service.PrepareAsync(context, commands, cancellationToken);
}
