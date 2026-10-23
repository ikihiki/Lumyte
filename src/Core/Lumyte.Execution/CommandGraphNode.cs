namespace Lumyte.Execution;

internal sealed class CommandGraphNode<TContext>(WorkDefinition<TContext, Unit> work) : GraphNode<TContext>(work.Module.Runtime)
    where TContext : struct
{
    internal override int Length(in TContext context) => 1;

    internal override int WorkerCount(int length, int concurrency) => 1;

    internal override QueuedWork[] Create(GraphState<TContext> state, int node, int length, int workers) => [new GraphCommand<TContext>(state, node, work)];
}
