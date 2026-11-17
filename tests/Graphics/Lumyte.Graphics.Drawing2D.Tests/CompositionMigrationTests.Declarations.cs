using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises passive declarations and independent runtime processors.</summary>
public sealed partial class CompositionMigrationTests
{
    private sealed class SharedDeclaration<T>(string name, Func<IGraphicDevice, T> create) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>, IPipelineDeclaration2D<FillRoundedRectCommand2D>, IPipelineDeclaration2D<FillCircleCommand2D>, IPipelineDeclaration2D<DrawLineCommand2D>, IPipelineDeclaration2D<FillPathCommand2D>, IPipelineDeclaration2D<StrokePathCommand2D>, IPipelineDeclaration2D<DrawImageCommand2D>, IPipelineDeclaration2D<DrawCoverageMaskCommand2D>, IPipelineDeclaration2D<DrawDistanceFieldCommand2D>, IPipelineDeclaration2D<DrawMeshCommand2D>, IPipelineDeclaration2D<DrawPaintGraphCommand2D>
        where T : class, IDisposable
    {
        public override string Name => name;

        public override Type ProcessorType => typeof(T);

        protected override object CreateElement(PipelineBuildContext2D context) => create(context.Device);
    }
}
