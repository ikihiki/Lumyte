namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class CoverageMembershipProcessor2D(CoverageMembershipService2D service) : PipelineElement2D,
    IFillRectRenderer2D,
    IPipelineAdmission2D<FillRectCommand2D>,
    IFillRoundedRectRenderer2D,
    IPipelineAdmission2D<FillRoundedRectCommand2D>,
    IFillCircleRenderer2D,
    IPipelineAdmission2D<FillCircleCommand2D>,
    IDrawLineRenderer2D,
    IPipelineAdmission2D<DrawLineCommand2D>,
    IFillPathRenderer2D,
    IPipelineAdmission2D<FillPathCommand2D>,
    IStrokePathRenderer2D,
    IPipelineAdmission2D<StrokePathCommand2D>,
    IDrawImageRenderer2D,
    IPipelineAdmission2D<DrawImageCommand2D>,
    IDrawCoverageMaskRenderer2D,
    IPipelineAdmission2D<DrawCoverageMaskCommand2D>,
    IDrawDistanceFieldRenderer2D,
    IPipelineAdmission2D<DrawDistanceFieldCommand2D>,
    IDrawMeshRenderer2D,
    IPipelineAdmission2D<DrawMeshCommand2D>,
    IDrawPaintGraphRenderer2D,
    IPipelineAdmission2D<DrawPaintGraphCommand2D>
{
    internal CoverageMembershipService2D Service => service;

    bool IPipelineAdmission2D<FillRectCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<FillRoundedRectCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillRoundedRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<FillCircleCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillCircleRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<DrawLineCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IDrawLineRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<FillPathCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillPathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<StrokePathCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IStrokePathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<DrawImageCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IDrawImageRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<DrawCoverageMaskCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IDrawCoverageMaskRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<DrawDistanceFieldCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IDrawDistanceFieldRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<DrawMeshCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IDrawMeshRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);

    bool IPipelineAdmission2D<DrawPaintGraphCommand2D>.CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands)
        => CoverageMembershipAdmission2D.CanPrepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IDrawPaintGraphRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken)
        => PreparedRecordBatch2D.PrepareAsync(context.WithShapeMembership(service), commands, cancellationToken);
}
