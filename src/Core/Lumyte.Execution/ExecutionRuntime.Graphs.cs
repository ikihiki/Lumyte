using System.Buffers;
using System.Runtime.CompilerServices;

namespace Lumyte.Execution;

/// <summary>Implements bounded execution scheduling and admission.</summary>
public sealed partial class ExecutionRuntime
{
    private readonly List<GraphState> _graphs = [];
    private int _graphCursor;

    internal bool CancelPending => _cancelPending;

    private bool HasGraphPropagation => _graphs.Any(graph => graph.HasPropagation);

    /// <summary>Atomically reserves a graph, including all future owner delivery slots.</summary>
    /// <typeparam name="TContext">The shared graph input type.</typeparam>
    /// <param name="graph">The immutable local graph.</param>
    /// <param name="context">The shared input.</param>
    /// <param name="run">The accepted run, or null on shortage.</param>
    /// <param name="token">Cancellation before node or chunk start.</param>
    /// <returns>False only on retained capacity shortage.</returns>
    public bool TrySchedule<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken token = default)
        where TContext : struct
    {
        VerifyOutsideHandler();
        ArgumentNullException.ThrowIfNull(graph);
        token.ThrowIfCancellationRequested();
        if (graph.Nodes[0].Runtime != this)
        {
            throw new ArgumentException("Graph belongs to another runtime.", nameof(graph));
        }

        lock (Sync)
        {
            RequireRunning();
        }

        int[] lengths = ArrayPool<int>.Shared.Rent(graph.NodeCount);
        try
        {
            int commands = 0;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                lengths[i] = graph.Nodes[i].Length(in context);
                if (lengths[i] < 0)
                {
                    throw new ArgumentException("A range length cannot be negative.", nameof(context));
                }

                commands = checked(commands + graph.Nodes[i].WorkerCount(lengths[i], _maxCpu));
            }

            int units = checked(graph.NodeCount + graph.EdgeCount + commands);
            long bytes = checked(Unsafe.SizeOf<TContext>() + 256L + (graph.NodeCount * 128L) + (graph.EdgeCount * 8L) + (commands * 128L));
            lock (Sync)
            {
                RequireRunning();
                if (!Reserve(1, commands, bytes, false, units))
                {
                    run = null;
                    return false;
                }

                GraphState<TContext> state;
                try
                {
                    run = new JobRun(this, commands, units, bytes, token);
                    state = new GraphState<TContext>(this, run, graph, in context, lengths, token, _maxCpu);
                    run.Attach(state);
                    _graphs.Add(state);
                }
                catch
                {
                    Release(1, commands, bytes, false, units);
                    throw;
                }

                if (_pending == 0)
                {
                    _quiet = null;
                }

                _pending += commands + 1; // Future work plus one bounded propagation controller.
                Notify();
                return true;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(lengths, true);
        }
    }

    internal void FinishGraphPending() => FinishPending();

    private bool PumpGraphPropagation()
    {
        for (int i = 0; i < _graphs.Count; i++)
        {
            int index = _graphCursor++ % _graphs.Count;
            _graphCursor %= _graphs.Count;
            GraphState state = _graphs[index];
            if (state.Run.IsCompleted)
            {
                _graphs.RemoveAt(index);
                _graphCursor = 0;
                return true;
            }

            if (state.HasPropagation)
            {
                state.Step();
                return true;
            }
        }

        return false;
    }
}
