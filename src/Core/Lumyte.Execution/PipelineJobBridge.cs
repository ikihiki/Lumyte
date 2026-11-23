namespace Lumyte.Execution;

internal static class PipelineJobBridge
{
    internal static JobGraph<TContext> Create<TContext>(ExecutionRuntime runtime, Func<TContext, CancellationToken, ValueTask<Unit>>[] actions, int[][] dependencies)
        where TContext : struct
    {
        if (actions.Length == 0 || actions.Length != dependencies.Length)
        {
            throw new ArgumentException("A lowered graph must be nonempty and complete.");
        }

        var nodes = new GraphNode<TContext>[actions.Length];
        int[] incoming = new int[actions.Length];
        List<int>[] outgoing = Enumerable.Range(0, actions.Length).Select(_ => new List<int>()).ToArray();
        int edges = 0;
        for (int i = 0; i < actions.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(actions[i]);
            nodes[i] = new LoweredNode<TContext>(runtime, actions[i]);
            foreach (int before in dependencies[i].Distinct())
            {
                if (before < 0 || before >= i)
                {
                    throw new ArgumentException("Lowered dependencies must precede their consumer.");
                }

                incoming[i]++;
                outgoing[before].Add(i);
                edges = checked(edges + 1);
            }
        }

        return new JobGraph<TContext>(nodes, incoming, outgoing.Select(value => value.ToArray()).ToArray(), edges);
    }

    private sealed class LoweredNode<TContext>(ExecutionRuntime runtime, Func<TContext, CancellationToken, ValueTask<Unit>> action) : GraphNode<TContext>(runtime)
        where TContext : struct
    {
        internal override int Length(in TContext context) => 1;

        internal override int WorkerCount(int length, int concurrency) => 1;

        internal override void Create(GraphState<TContext> state, int node, int length, Span<QueuedWork> workers)
            => workers[0] = new PendingWorkflow<TContext, Unit>(Runtime, state.Context, state.Token, action, (error, canceled) => state.WorkerFinished(node, error, canceled), () => state.IsCancellationRequested);
    }
}
