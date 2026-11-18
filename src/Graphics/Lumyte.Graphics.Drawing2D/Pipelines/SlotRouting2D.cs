namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Creates kind-local ordered choices; unrelated kinds are never inspected.</summary>
internal static class SlotRouting2D
{
    /// <summary>Creates an ordered choice for only FillRect contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IFillRectRenderer2D FillRect(params SlotCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<FillRectCommand2D> ? candidates[0].Backend : new FillRectChoice(candidates);

    /// <summary>Creates an ordered choice for only FillRoundedRect contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IFillRoundedRectRenderer2D FillRoundedRect(params SlotCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<FillRoundedRectCommand2D> ? candidates[0].Backend : new FillRoundedRectChoice(candidates);

    /// <summary>Creates an ordered choice for only FillCircle contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IFillCircleRenderer2D FillCircle(params SlotCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<FillCircleCommand2D> ? candidates[0].Backend : new FillCircleChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawLine contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IDrawLineRenderer2D DrawLine(params SlotCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<DrawLineCommand2D> ? candidates[0].Backend : new DrawLineChoice(candidates);

    /// <summary>Creates an ordered choice for only FillPath contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IFillPathRenderer2D FillPath(params SlotCandidate2D<FillPathCommand2D, IFillPathRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<FillPathCommand2D> ? candidates[0].Backend : new FillPathChoice(candidates);

    /// <summary>Creates an ordered choice for only StrokePath contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IStrokePathRenderer2D StrokePath(params SlotCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<StrokePathCommand2D> ? candidates[0].Backend : new StrokePathChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawImage contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IDrawImageRenderer2D DrawImage(params SlotCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<DrawImageCommand2D> ? candidates[0].Backend : new DrawImageChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawCoverageMask contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IDrawCoverageMaskRenderer2D DrawCoverageMask(params SlotCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<DrawCoverageMaskCommand2D> ? candidates[0].Backend : new DrawCoverageMaskChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawDistanceField contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IDrawDistanceFieldRenderer2D DrawDistanceField(params SlotCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<DrawDistanceFieldCommand2D> ? candidates[0].Backend : new DrawDistanceFieldChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawMesh contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IDrawMeshRenderer2D DrawMesh(params SlotCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<DrawMeshCommand2D> ? candidates[0].Backend : new DrawMeshChoice(candidates);

    /// <summary>Creates an ordered choice for only DrawPaintGraph contiguous compatible commands.</summary>
    /// <param name="candidates">Explicit nonempty borrowed candidates, copied at construction.</param>
    /// <returns>The original typed replacement interface.</returns>
    internal static IDrawPaintGraphRenderer2D DrawPaintGraph(params SlotCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>[] candidates) => candidates.Length == 1 && candidates[0].Backend is not IPipelineAdmission2D<DrawPaintGraphCommand2D> ? candidates[0].Backend : new DrawPaintGraphChoice(candidates);

    private static SlotCandidate2D<TCommand, TBackend>[] Copy<TCommand, TBackend>(SlotCandidate2D<TCommand, TBackend>[] candidates)
        where TCommand : DrawingCommand2D
        where TBackend : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Length == 0 || candidates.Any(c => c is null || c.Backend is null))
        {
            throw new ArgumentException("An ordered choice needs explicit non-null candidates.", nameof(candidates));
        }

        return (SlotCandidate2D<TCommand, TBackend>[])candidates.Clone();
    }

    private static TBackend Select<TCommand, TBackend>(SlotCandidate2D<TCommand, TBackend>[] candidates, RenderPreparationContext2D context, ReadOnlyMemory<TCommand> commands)
        where TCommand : DrawingCommand2D
        where TBackend : class
    {
        foreach (SlotCandidate2D<TCommand, TBackend> candidate in candidates)
        {
            if (candidate.Backend is not IPipelineAdmission2D<TCommand> admission || admission.CanPrepare(context, commands))
            {
                return candidate.Backend;
            }
        }

        throw new NotSupportedException("Every candidate for the selected command kind rejected the batch.");
    }

    private sealed class FillRectChoice(SlotCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] candidates) : PipelineElement2D, IFillRectRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<FillRectCommand2D, IFillRectRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class FillRoundedRectChoice(SlotCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>[] candidates) : PipelineElement2D, IFillRoundedRectRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<FillRoundedRectCommand2D, IFillRoundedRectRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class FillCircleChoice(SlotCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>[] candidates) : PipelineElement2D, IFillCircleRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<FillCircleCommand2D, IFillCircleRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawLineChoice(SlotCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>[] candidates) : PipelineElement2D, IDrawLineRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<DrawLineCommand2D, IDrawLineRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class FillPathChoice(SlotCandidate2D<FillPathCommand2D, IFillPathRenderer2D>[] candidates) : PipelineElement2D, IFillPathRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<FillPathCommand2D, IFillPathRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class StrokePathChoice(SlotCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>[] candidates) : PipelineElement2D, IStrokePathRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<StrokePathCommand2D, IStrokePathRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawImageChoice(SlotCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>[] candidates) : PipelineElement2D, IDrawImageRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<DrawImageCommand2D, IDrawImageRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawCoverageMaskChoice(SlotCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>[] candidates) : PipelineElement2D, IDrawCoverageMaskRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<DrawCoverageMaskCommand2D, IDrawCoverageMaskRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawDistanceFieldChoice(SlotCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>[] candidates) : PipelineElement2D, IDrawDistanceFieldRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<DrawDistanceFieldCommand2D, IDrawDistanceFieldRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawMeshChoice(SlotCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>[] candidates) : PipelineElement2D, IDrawMeshRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<DrawMeshCommand2D, IDrawMeshRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class DrawPaintGraphChoice(SlotCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>[] candidates) : PipelineElement2D, IDrawPaintGraphRenderer2D, ICommandChoice2D
    {
        private readonly SlotCandidate2D<DrawPaintGraphCommand2D, IDrawPaintGraphRenderer2D>[] _candidates = Copy(candidates);

        /// <inheritdoc/>
        IReadOnlyList<Type> ICommandChoice2D.CandidateTypes => Array.AsReadOnly(_candidates.Select(candidate => candidate.Backend.GetType()).ToArray());

        IReadOnlyList<object> ICommandChoice2D.CandidateProcessors => Array.AsReadOnly(_candidates.Select(candidate => (object)candidate.Backend).ToArray());

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Select(_candidates, context, commands).PrepareAsync(context, commands, cancellationToken);
        }
    }
}
