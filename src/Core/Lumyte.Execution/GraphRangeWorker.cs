namespace Lumyte.Execution;

internal sealed class GraphRangeWorker<TContext>(GraphState<TContext> state, int node, RangeDefinition<TContext> work, RangeClaim range) : QueuedWork(null, state.Token)
    where TContext : struct
{
    internal override bool IsCanceled => state.IsCancellationRequested;

    internal override void Perform(in WorkContext context)
    {
        if (range.TryClaim(out int first, out int end))
        {
            work.Invoke(in state.Context, first, end);
        }
    }

    internal override WorkFinish Finish(Exception? error)
    {
        if (error is null && !state.IsCancellationRequested && range.HasMore)
        {
            return WorkFinish.Requeue;
        }

        bool incomplete = error is null && state.IsCancellationRequested && range.HasMore;
        state.WorkerFinished(node, incomplete ? new OperationCanceledException(Token) : error, CanceledBeforeStart || incomplete);
        return WorkFinish.GraphWorkerCompleted;
    }
}
