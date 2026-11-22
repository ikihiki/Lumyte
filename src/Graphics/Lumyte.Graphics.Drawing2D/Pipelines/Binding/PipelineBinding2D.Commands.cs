namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed partial class PipelineBinding2D
{
    private sealed class CommandAdapter(PipelineBinding2D binding) : IFillRectRenderer2D, IFillRoundedRectRenderer2D, IFillCircleRenderer2D, IDrawLineRenderer2D, IFillPathRenderer2D, IStrokePathRenderer2D, IDrawImageRenderer2D, IDrawCoverageMaskRenderer2D, IDrawDistanceFieldRenderer2D, IDrawMeshRenderer2D, IDrawPaintGraphRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default)
            => binding.PrepareAsync(context, commands.ToArray().Cast<DrawingCommand2D>().ToArray(), cancellationToken);
    }
}
