namespace Lumyte.Execution;

internal sealed class GraphRangeWorker<TContext>(GraphState<TContext> state, int node, RangeDefinition<TContext> work, RangeClaim range) : QueuedWork(null, state.Token)
    where TContext : struct
{
    private bool _requeue;

    internal override bool IsCanceled => state.IsCancellationRequested;

    internal override bool KeepAlive => _requeue;

    internal override void Perform(in WorkContext context)
    {
        if (range.TryClaim(out int first, out int end))
        {
            work.Invoke(in state.Context, first, end);
        }
    }

    internal override void Finish(Exception? error)
    {
        lock (state.Runtime.Sync)
        {
            _requeue = error is null && !state.IsCancellationRequested && range.HasMore;
            if (_requeue)
            {
                state.Runtime.QueueReady(this);
            }
            else
            {
                bool incomplete = error is null && state.IsCancellationRequested && range.HasMore;
                state.WorkerFinished(node, incomplete ? new OperationCanceledException(Token) : error, CanceledBeforeStart || incomplete);
            }
        }
    }
}
