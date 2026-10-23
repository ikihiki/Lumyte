namespace Lumyte.Execution;

internal sealed class RangeGraphNode<TContext>(RangeDefinition<TContext> work, int batchSize) : GraphNode<TContext>(work.Module.Runtime)
    where TContext : struct
{
    internal override int Length(in TContext context) => work.GetLength(in context);

    internal override int WorkerCount(int length, int concurrency) => (int)Math.Min(concurrency, ((long)length + batchSize - 1) / batchSize);

    internal override QueuedWork[] Create(GraphState<TContext> state, int node, int length, int workers)
    {
        var range = new RangeClaim(length, batchSize);
        var items = new QueuedWork[workers];
        for (int i = 0; i < workers; i++)
        {
            items[i] = new GraphRangeWorker<TContext>(state, node, work, range);
        }

        return items;
    }
}
