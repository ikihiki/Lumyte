namespace Lumyte.Execution;

internal sealed class GraphState<TContext> : GraphState
    where TContext : struct
{
    private readonly JobGraph<TContext> _graph;
    private readonly QueuedWork[] _workers;
    private readonly NodeState[] _nodes;
    private readonly Queue<int> _completedNodes = new();
    private TContext _context;
    private int _initialCursor;
    private int _cancelCursor;
    private int _propagationCursor;
    private int _finishedNodes;

    internal GraphState(ExecutionRuntime runtime, JobRun run, JobGraph<TContext> graph, in TContext context, int[] lengths, CancellationToken token, int concurrency)
        : base(runtime, run, token)
    {
        _graph = graph;
        Context = context;
        _nodes = new NodeState[graph.NodeCount];
        int workerCount = 0;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            int count = graph.Nodes[i].WorkerCount(lengths[i], concurrency);
            _nodes[i] = new NodeState
            {
                Incoming = graph.Incoming[i],
                Remaining = count,
                WorkerStart = workerCount,
                WorkerCount = count,
            };
            workerCount = checked(workerCount + count);
        }

        _workers = new QueuedWork[workerCount];
        for (int i = 0; i < graph.NodeCount; i++)
        {
            ref NodeState node = ref _nodes[i];
            graph.Nodes[i].Create(this, i, lengths[i], _workers.AsSpan(node.WorkerStart, node.WorkerCount));
        }
    }

    internal ref TContext Context => ref _context;

    internal override bool HasPropagation => _initialCursor < _nodes.Length || _completedNodes.Count > 0 || (IsCancellationRequested && _cancelCursor < _nodes.Length);

    internal void WorkerFinished(int node, Exception? error, bool canceled)
    {
        Run.Record(error, canceled);
        if (--_nodes[node].Remaining == 0)
        {
            CompleteNode(node);
        }
    }

    internal override bool Step()
    {
        if (IsCancellationRequested && _cancelCursor < _nodes.Length)
        {
            int node = _cancelCursor++;
            ref NodeState state = ref _nodes[node];
            if (state.Status == 0)
            {
                state.Status = 2;
                foreach (QueuedWork worker in _workers.AsSpan(state.WorkerStart, state.WorkerCount))
                {
                    worker.Definition = null;
                }

                Run.Record(new OperationCanceledException(Token), true);
                _completedNodes.Enqueue(node);
            }
        }
        else if (_initialCursor < _nodes.Length)
        {
            int node = _initialCursor++;
            if (_nodes[node].Incoming == 0 && _nodes[node].Status == 0)
            {
                QueueNode(node);
            }
        }
        else if (_completedNodes.TryPeek(out int node))
        {
            int[] successors = _graph.Outgoing[node];
            if (_propagationCursor < successors.Length)
            {
                int next = successors[_propagationCursor++];
                if (--_nodes[next].Incoming == 0 && _nodes[next].Status == 0 && !IsCancellationRequested)
                {
                    QueueNode(next);
                }
            }
            else
            {
                _completedNodes.Dequeue();
                _propagationCursor = 0;
                if (++_finishedNodes == _nodes.Length)
                {
                    Context = default;
                    Run.Complete();
                    return true;
                }
            }
        }

        return false;
    }

    internal override void Clear()
    {
        Context = default;
        Array.Clear(_workers);
    }

    private void QueueNode(int node)
    {
        ref NodeState state = ref _nodes[node];
        state.Status = 1;
        foreach (QueuedWork worker in _workers.AsSpan(state.WorkerStart, state.WorkerCount))
        {
            Runtime.QueueReady(worker);
        }

        if (state.WorkerCount == 0)
        {
            CompleteNode(node);
        }

        Runtime.Notify(false);
    }

    private void CompleteNode(int node)
    {
        _nodes[node].Status = 2;
        _completedNodes.Enqueue(node);
    }

    private struct NodeState
    {
        internal int Incoming;
        internal int Remaining;
        internal int WorkerStart;
        internal int WorkerCount;
        internal byte Status;
    }
}
