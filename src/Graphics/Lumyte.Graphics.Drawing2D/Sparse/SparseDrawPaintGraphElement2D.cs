using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class SparseDrawPaintGraphElement2D(SparseCpuService2D service) : PipelineElement2D, IDrawPaintGraphRenderer2D, IPipelineAdmission2D<DrawPaintGraphCommand2D>
{
    internal SparseCpuService2D Service => service;

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands)
        => service.CanPrepare(context);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default)
        => service.PrepareAsync(context, commands, cancellationToken);
}
