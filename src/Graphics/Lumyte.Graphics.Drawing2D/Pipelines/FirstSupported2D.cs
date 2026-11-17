namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Creates kind-local ordered choices; unrelated kinds are never inspected.</summary>
public static class FirstSupported2D
{
    /// <summary>Creates an ordered choice for only FillRect contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IFillRectRenderer2D FillRect(params CommandCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] candidates) => new FillRectChoice(candidates);

    /// <summary>Creates an ordered choice for only FillRoundedRect contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IFillRoundedRectRenderer2D FillRoundedRect(params CommandCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>[] candidates) => new FillRoundedRectChoice(candidates);

    /// <summary>Creates an ordered choice for only FillCircle contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IFillCircleRenderer2D FillCircle(params CommandCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>[] candidates) => new FillCircleChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawLine contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IDrawLineRenderer2D DrawLine(params CommandCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>[] candidates) => new DrawLineChoice(candidates);

    /// <summary>Creates an ordered choice for only FillPath contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IFillPathRenderer2D FillPath(params CommandCandidate2D<FillPathCommand2D, IFillPathRenderer2D>[] candidates) => new FillPathChoice(candidates);

    /// <summary>Creates an ordered choice for only StrokePath contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IStrokePathRenderer2D StrokePath(params CommandCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>[] candidates) => new StrokePathChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawImage contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IDrawImageRenderer2D DrawImage(params CommandCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>[] candidates) => new DrawImageChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawCoverageMask contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IDrawCoverageMaskRenderer2D DrawCoverageMask(params CommandCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>[] candidates) => new DrawCoverageMaskChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawDistanceField contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IDrawDistanceFieldRenderer2D DrawDistanceField(params CommandCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>[] candidates) => new DrawDistanceFieldChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawMesh contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IDrawMeshRenderer2D DrawMesh(params CommandCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>[] candidates) => new DrawMeshChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawPaintGraph contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    public static IDrawPaintGraphRenderer2D DrawPaintGraph(params CommandCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>[] candidates) => new DrawPaintGraphChoice(candidates);

    internal static Type GetProcessorType(DrawKind2D kind) => kind switch
    {
        DrawKind2D.FillRect => typeof(FillRectChoice),
        DrawKind2D.FillRoundedRect => typeof(FillRoundedRectChoice),
        DrawKind2D.FillCircle => typeof(FillCircleChoice),
        DrawKind2D.DrawLine => typeof(DrawLineChoice),
        DrawKind2D.FillPath => typeof(FillPathChoice),
        DrawKind2D.StrokePath => typeof(StrokePathChoice),
        DrawKind2D.DrawImage => typeof(DrawImageChoice),
        DrawKind2D.DrawCoverageMask => typeof(DrawCoverageMaskChoice),
        DrawKind2D.DrawDistanceField => typeof(DrawDistanceFieldChoice),
        DrawKind2D.DrawMesh => typeof(DrawMeshChoice),
        DrawKind2D.DrawPaintGraph => typeof(DrawPaintGraphChoice),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static CommandCandidate2D<TCommand, TBackend>[] Copy<TCommand, TBackend>(CommandCandidate2D<TCommand, TBackend>[] candidates)
        where TCommand : DrawingCommand2D
        where TBackend : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Length == 0 || candidates.Any(c => c is null || c.Supports is null || c.Backend is null))
        {
            throw new ArgumentException("An ordered choice needs explicit non-null candidates.", nameof(candidates));
        }

        return (CommandCandidate2D<TCommand, TBackend>[])candidates.Clone();
    }

    private static TBackend Select<TCommand, TBackend>(CommandCandidate2D<TCommand, TBackend>[] candidates, RenderPreparationContext2D context, ReadOnlyMemory<TCommand> commands)
        where TCommand : DrawingCommand2D
        where TBackend : class
    {
        foreach (CommandCandidate2D<TCommand, TBackend> candidate in candidates)
        {
            if (candidate.Supports(context, commands))
            {
                return candidate.Backend;
            }
        }

        throw new NotSupportedException("Every candidate for the selected command kind rejected the batch.");
    }

    private sealed class FillRectChoice(CommandCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] candidates) : PipelineElement2D, IFillRectRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class FillRoundedRectChoice(CommandCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>[] candidates) : PipelineElement2D, IFillRoundedRectRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class FillCircleChoice(CommandCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>[] candidates) : PipelineElement2D, IFillCircleRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawLineChoice(CommandCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>[] candidates) : PipelineElement2D, IDrawLineRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class FillPathChoice(CommandCandidate2D<FillPathCommand2D, IFillPathRenderer2D>[] candidates) : PipelineElement2D, IFillPathRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<FillPathCommand2D, IFillPathRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class StrokePathChoice(CommandCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>[] candidates) : PipelineElement2D, IStrokePathRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawImageChoice(CommandCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>[] candidates) : PipelineElement2D, IDrawImageRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawCoverageMaskChoice(CommandCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>[] candidates) : PipelineElement2D, IDrawCoverageMaskRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawDistanceFieldChoice(CommandCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>[] candidates) : PipelineElement2D, IDrawDistanceFieldRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawMeshChoice(CommandCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>[] candidates) : PipelineElement2D, IDrawMeshRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawPaintGraphChoice(CommandCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>[] candidates) : PipelineElement2D, IDrawPaintGraphRenderer2D, ICommandChoice2D
    {
        private readonly CommandCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }
}
