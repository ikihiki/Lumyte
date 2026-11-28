namespace Lumyte.Execution;

/// <summary>Adapts finite CPU DAG callbacks to the existing runtime job graph.</summary>
public static class PipelineJobBridge
{
    /// <summary>Creates an append-ordered graph without a separate scheduler.</summary>
    /// <typeparam name="TContext">The invocation context.</typeparam>
    /// <param name="runtime">The borrowed execution runtime.</param>
    /// <param name="actions">The finite node actions captured by the graph.</param>
    /// <param name="dependencies">Producer indices strictly before each consumer.</param>
    /// <returns>The immutable runtime graph.</returns>
    public static JobGraph<TContext> Create<TContext>(ExecutionRuntime runtime, Func<TContext, CancellationToken, ValueTask<Unit>>[] actions, int[][] dependencies)
        where TContext : struct
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(dependencies);
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
            ArgumentNullException.ThrowIfNull(dependencies[i]);
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

    /// <summary>Queries the prepared runtime backend at admission, not at pipeline build time.</summary>
    /// <param name="runtime">The borrowed runtime.</param>
    /// <returns>Whether cooperative pipeline evaluation can use one existing workflow.</returns>
    public static bool IsCooperative(ExecutionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (runtime.Sync)
        {
            return runtime.IsCooperative;
        }
    }

    /// <summary>Creates coarse workflows with cancellation-ordered logical stage checkpoints.</summary>
    /// <typeparam name="TContext">The invocation context.</typeparam>
    /// <param name="runtime">The borrowed execution runtime.</param>
    /// <param name="actions">The finite compiled segment callbacks.</param>
    /// <param name="dependencies">Segment producer indices preceding their consumer.</param>
    /// <param name="retainedBytes">Additional finite per-run managed metadata bytes retained until JobRun disposal. Physical segment nodes, edges and workers still determine graph units; callers separately bound logical program size.</param>
    /// <returns>The immutable runtime graph.</returns>
    public static JobGraph<TContext> CreateCooperative<TContext>(ExecutionRuntime runtime, Func<TContext, PipelineJobScope, CancellationToken, ValueTask<Unit>>[] actions, int[][] dependencies, long retainedBytes)
        where TContext : struct
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentOutOfRangeException.ThrowIfNegative(retainedBytes);
        var placeholders = new Func<TContext, CancellationToken, ValueTask<Unit>>[actions.Length];
        for (int i = 0; i < actions.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(actions[i]);
            placeholders[i] = static (_, _) => default;
        }

        JobGraph<TContext> graph = Create(runtime, placeholders, dependencies);
        for (int i = 0; i < actions.Length; i++)
        {
            graph.Nodes[i] = new CooperativeNode<TContext>(runtime, actions[i]);
        }

        return new JobGraph<TContext>(graph.Nodes, graph.Incoming, graph.Outgoing, graph.EdgeCount, retainedBytes);
    }

    private sealed class CooperativeNode<TContext>(ExecutionRuntime runtime, Func<TContext, PipelineJobScope, CancellationToken, ValueTask<Unit>> action) : GraphNode<TContext>(runtime)
        where TContext : struct
    {
        internal override int Length(in TContext context) => 1;

        internal override int WorkerCount(int length, int concurrency) => 1;

        internal override void Create(GraphState<TContext> state, int node, int length, Span<QueuedWork> workers)
        {
            var scope = new PipelineJobScope(state);
            workers[0] = new PendingWorkflow<TContext, Unit>(Runtime, state.Context, state.Token, (context, token) => InvokeAsync(context, scope, token), (error, canceled) => state.WorkerFinished(node, error, canceled), () => state.IsCancellationRequested);
        }

        private static async ValueTask<Unit> AwaitCompletionAsync(ValueTask<Unit> operation, PipelineJobScope scope)
        {
            try
            {
                return await operation;
            }
            finally
            {
                scope.Close();
            }
        }

        private ValueTask<Unit> InvokeAsync(TContext context, PipelineJobScope scope, CancellationToken token)
        {
            try
            {
                ValueTask<Unit> operation = action(context, scope, token);
                if (!operation.IsCompletedSuccessfully)
                {
                    return AwaitCompletionAsync(operation, scope);
                }

                Unit result = operation.Result;
                scope.Close();
                return new ValueTask<Unit>(result);
            }
            catch
            {
                scope.Close();
                throw;
            }
        }
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
