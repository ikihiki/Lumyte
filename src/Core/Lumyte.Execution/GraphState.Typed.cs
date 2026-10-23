namespace Lumyte.Execution;

internal sealed class GraphState<TContext> : GraphState
    where TContext : struct
{
    private readonly JobGraph<TContext> _graph;
    private readonly QueuedWork[][] _workers;
    private readonly int[] _incoming;
    private readonly int[] _remaining;
    private readonly byte[] _states;
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
        _incoming = (int[])graph.Incoming.Clone();
        _remaining = new int[graph.NodeCount];
        _states = new byte[graph.NodeCount];
        _workers = new QueuedWork[graph.NodeCount][];
        for (int i = 0; i < graph.NodeCount; i++)
        {
            int count = graph.Nodes[i].WorkerCount(lengths[i], concurrency);
            _workers[i] = graph.Nodes[i].Create(this, i, lengths[i], count);
            _remaining[i] = count;
        }
    }

    internal ref TContext Context => ref _context;

    internal override bool HasPropagation => _initialCursor < _states.Length || _completedNodes.Count > 0 || (IsCancellationRequested && _cancelCursor < _states.Length);

    internal void WorkerFinished(int node, Exception? error, bool canceled)
    {
        Run.Record(error, canceled);
        if (--_remaining[node] == 0)
        {
            CompleteNode(node);
        }
    }

    internal override bool Step()
    {
        if (IsCancellationRequested && _cancelCursor < _states.Length)
        {
            int node = _cancelCursor++;
            if (_states[node] == 0)
            {
                _states[node] = 2;
                foreach (QueuedWork worker in _workers[node])
                {
                    worker.Definition = null;
                }

                Run.Record(new OperationCanceledException(Token), true);
                _completedNodes.Enqueue(node);
            }
        }
        else if (_initialCursor < _states.Length)
        {
            int node = _initialCursor++;
            if (_incoming[node] == 0 && _states[node] == 0)
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
                if (--_incoming[next] == 0 && _states[next] == 0 && !IsCancellationRequested)
                {
                    QueueNode(next);
                }
            }
            else
            {
                _completedNodes.Dequeue();
                _propagationCursor = 0;
                if (++_finishedNodes == _states.Length)
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
        foreach (QueuedWork[] workers in _workers)
        {
            Array.Clear(workers);
        }
    }

    private void QueueNode(int node)
    {
        _states[node] = 1;
        foreach (QueuedWork worker in _workers[node])
        {
            Runtime.QueueReady(worker);
        }

        if (_workers[node].Length == 0)
        {
            CompleteNode(node);
        }

        Runtime.Notify(false);
    }

    private void CompleteNode(int node)
    {
        _states[node] = 2;
        _completedNodes.Enqueue(node);
    }
}
