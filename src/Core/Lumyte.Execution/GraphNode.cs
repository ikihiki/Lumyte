namespace Lumyte.Execution;

internal abstract class GraphNode<TContext>(ExecutionRuntime runtime)
    where TContext : struct
{
    internal ExecutionRuntime Runtime { get; } = runtime;

    internal abstract int Length(in TContext context);

    internal abstract int WorkerCount(int length, int concurrency);

    internal abstract void Create(GraphState<TContext> state, int node, int length, Span<QueuedWork> workers);
}
