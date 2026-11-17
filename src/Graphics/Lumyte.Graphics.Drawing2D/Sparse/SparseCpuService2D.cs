using Lumyte.Graphics.Drawing2D.Pipelines;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class SparseCpuService2D(SparseCpuOptions2D options) : PipelineSharedService2D
{
    internal SparseGeometryCache2D Geometry { get; } = new(options.MaxCachedBytes, options.MaxCachedAssets, options.MaxStrips, options.MaxBoundaryMasks);

    public override void Dispose() => Geometry.Clear();

    internal bool CanPrepare(RenderPreparationContext2D context)
        => context.Target.Description.Width <= 32768 && context.Target.Description.Height <= 32768;

    internal async ValueTask<IPreparedCommandBatch2D> PrepareAsync<TCommand>(RenderPreparationContext2D context, ReadOnlyMemory<TCommand> commands, CancellationToken cancellationToken)
        where TCommand : DrawingCommand2D
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.EnforceWorkingBudget)
        {
            context = context.WithWorkingLimit(context.MaxWorkingBytes);
        }

        context.ArgumentPlan.EnableWorkingReferenceBudget();
        DrawingWorkingBudget2D.ValidateCumulative(context.OtherWorkingBytes, context.ArgumentPlan.EstimatedBytes, context.MaxWorkingBytes);
        long scratch = checked(1024L + (commands.Length * 512L));
        if (scratch > context.MaxWorkingBytes)
        {
            throw new NotSupportedException("Sparse typed command scratch exceeds the working budget.");
        }

        DrawingWorkingBudget2D.Validate(commands, context.Target, context.MaxWorkingBytes - context.OtherWorkingBytes - context.ArgumentPlan.EstimatedBytes - scratch);
        var current = new DrawingCommand2D[commands.Length];
        for (int i = 0; i < current.Length; i++)
        {
            current[i] = commands.Span[i];
        }

        SparseStripPlan2D plan = Geometry.GetSparse(new DrawList2D(current), context.Target, context.MaxWorkingBytes - context.OtherWorkingBytes - context.ArgumentPlan.EstimatedBytes - scratch, cancellationToken);
        if (plan.Strips.Length > options.MaxStrips || plan.BoundaryMasks.Length > options.MaxBoundaryMasks)
        {
            throw new NotSupportedException("Sparse output exceeds the explicit strip or boundary-mask limit.");
        }

        return await PreparedSparseStripBatch2D.PrepareAsync(context, plan with { WorkingBytes = checked(plan.WorkingBytes + scratch + Geometry.EstimatedBytes) }, cancellationToken).ConfigureAwait(false);
    }
}
