namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class MeshCoverageProcessor2D(MeshCoverageService2D service) : PipelineElement2D,
    IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>,
    IFillRoundedRectRenderer2D, IPipelineAdmission2D<FillRoundedRectCommand2D>,
    IFillCircleRenderer2D, IPipelineAdmission2D<FillCircleCommand2D>,
    IDrawLineRenderer2D, IPipelineAdmission2D<DrawLineCommand2D>,
    IFillPathRenderer2D, IPipelineAdmission2D<FillPathCommand2D>,
    IStrokePathRenderer2D, IPipelineAdmission2D<StrokePathCommand2D>,
    IDrawImageRenderer2D, IPipelineAdmission2D<DrawImageCommand2D>,
    IDrawCoverageMaskRenderer2D, IPipelineAdmission2D<DrawCoverageMaskCommand2D>,
    IDrawDistanceFieldRenderer2D, IPipelineAdmission2D<DrawDistanceFieldCommand2D>,
    IDrawPaintGraphRenderer2D, IPipelineAdmission2D<DrawPaintGraphCommand2D>
{
    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands) => Admit(context, commands.Length);

    public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default)
        => Prepare(context, commands, cancellationToken);

    private bool Admit(RenderPreparationContext2D context, int count) => count <= service.Options.MaxCommands && service.Supports(context.Device.Caps);

    private ValueTask<IPreparedCommandBatch2D> Prepare<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands, CancellationToken token)
        where T : DrawingCommand2D
    {
        if (!Admit(context, commands.Length))
        {
            throw new NotSupportedException("Mesh coverage candidate is unavailable or exceeds configured bounds.");
        }

        DrawingWorkingBudget2D.Validate(commands, context.Target, service.Options.MaxWorkingBytes);
        return PreparedRecordBatch2D.PrepareAsync(context.WithWorkingLimit(service.Options.MaxWorkingBytes), commands, token, service.Resources);
    }
}
