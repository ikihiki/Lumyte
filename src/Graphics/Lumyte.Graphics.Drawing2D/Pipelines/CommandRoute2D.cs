namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Immutable ordered declaration topology resolved only by the runtime compiler.</summary>
internal sealed class CommandRoute2D
{
    internal CommandRoute2D(DrawKind2D kind, PipelineDeclaration2D[] declarations)
    {
        Kind = kind;
        Declarations = Array.AsReadOnly((PipelineDeclaration2D[])declarations.Clone());
    }

    internal DrawKind2D Kind { get; }

    internal IReadOnlyList<PipelineDeclaration2D> Declarations { get; }

    internal PipelineOccurrence2D[] Occurrences { get; set; } = [];

    internal object Compile(BackendCompileScope2D scope) => Kind switch
    {
        DrawKind2D.FillRect => SlotRouting2D.FillRect(Occurrences.Select(occurrence => new SlotCandidate2D<FillRectCommand2D, IFillRectRenderer2D>((IFillRectRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.FillRoundedRect => SlotRouting2D.FillRoundedRect(Occurrences.Select(occurrence => new SlotCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>((IFillRoundedRectRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.FillCircle => SlotRouting2D.FillCircle(Occurrences.Select(occurrence => new SlotCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>((IFillCircleRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.DrawLine => SlotRouting2D.DrawLine(Occurrences.Select(occurrence => new SlotCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>((IDrawLineRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.FillPath => SlotRouting2D.FillPath(Occurrences.Select(occurrence => new SlotCandidate2D<FillPathCommand2D, IFillPathRenderer2D>((IFillPathRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.StrokePath => SlotRouting2D.StrokePath(Occurrences.Select(occurrence => new SlotCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>((IStrokePathRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.DrawImage => SlotRouting2D.DrawImage(Occurrences.Select(occurrence => new SlotCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>((IDrawImageRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.DrawCoverageMask => SlotRouting2D.DrawCoverageMask(Occurrences.Select(occurrence => new SlotCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>((IDrawCoverageMaskRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.DrawDistanceField => SlotRouting2D.DrawDistanceField(Occurrences.Select(occurrence => new SlotCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>((IDrawDistanceFieldRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.DrawMesh => SlotRouting2D.DrawMesh(Occurrences.Select(occurrence => new SlotCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>((IDrawMeshRenderer2D)scope.Resolve(occurrence))).ToArray()),
        DrawKind2D.DrawPaintGraph => SlotRouting2D.DrawPaintGraph(Occurrences.Select(occurrence => new SlotCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>((IDrawPaintGraphRenderer2D)scope.Resolve(occurrence))).ToArray()),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
    };
}
