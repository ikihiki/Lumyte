namespace Lumyte.Execution;

internal sealed class GraphCommand<TContext>(GraphState<TContext> state, int node, WorkDefinition<TContext, Unit> work) : QueuedWork(work, state.Token)
    where TContext : struct
{
    internal override bool IsCanceled => state.IsCancellationRequested;

    internal override void Perform(in WorkContext context) => work.Invoke(in state.Context, in context);

    internal override WorkFinish Finish(Exception? error)
    {
        state.WorkerFinished(node, error, CanceledBeforeStart);
        return WorkFinish.GraphWorkerCompleted;
    }
}
