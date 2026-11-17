using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises passive declarations and independent runtime processors.</summary>
public sealed partial class CompositionMigrationTests
{
    private sealed class SharedDeclaration<T>(string name, Func<IGraphicDevice, T> create) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>, IPipelineDeclaration2D<FillRoundedRectCommand2D>, IPipelineDeclaration2D<FillCircleCommand2D>, IPipelineDeclaration2D<DrawLineCommand2D>, IPipelineDeclaration2D<FillPathCommand2D>, IPipelineDeclaration2D<StrokePathCommand2D>, IPipelineDeclaration2D<DrawImageCommand2D>, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>, IPipelineDeclaration2D<DrawMeshCommand2D>, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        where T : PipelineSharedService2D
    {
        public override string Name => name;

        public override Type ProcessorType => typeof(ServiceProcessor<T>);

        protected override object CreateElement(PipelineBuildContext2D context) => new ServiceProcessor<T>(context.GetSharedService(new TestServiceRequest<T>(name, create)));
    }

    private sealed class TestServiceRequest<T>(string name, Func<IGraphicDevice, T> create) : PipelineServiceRequest2D<T, string>("test-manager", name, 1)
        where T : PipelineSharedService2D
    {
        protected override T CreateService(PipelineBuildContext2D context) => create(context.Device);
    }

    private sealed class ServiceProcessor<T>(T manager) : PipelineElement2D, IFillRectRenderer2D, IFillRoundedRectRenderer2D, IFillCircleRenderer2D, IDrawLineRenderer2D, IFillPathRenderer2D, IStrokePathRenderer2D, IDrawImageRenderer2D, IDrawCoverageMaskRenderer2D, IDrawDistanceFieldRenderer2D, IDrawMeshRenderer2D, IDrawPaintGraphRenderer2D
        where T : PipelineSharedService2D
    {
        internal T Manager => manager;

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillRectRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillRoundedRectRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillCircleRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawLineRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default) => ((IFillPathRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default) => ((IStrokePathRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawImageRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawCoverageMaskRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawDistanceFieldRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawMeshRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default) => ((IDrawPaintGraphRenderer2D)manager).PrepareAsync(context, commands, cancellationToken);
    }
}
