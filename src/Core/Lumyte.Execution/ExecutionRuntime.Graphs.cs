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

    /// <summary>Atomically admits a graph, reporting stopped, canceled and full admission as statuses.</summary>
    /// <typeparam name="TContext">The shared graph input type.</typeparam>
    /// <param name="graph">The immutable local graph.</param>
    /// <param name="context">The shared input.</param>
    /// <param name="run">The accepted run, or null on rejection.</param>
    /// <param name="cancellationToken">Cancellation before node or chunk start.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus Schedule<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken cancellationToken = default)
        where TContext : struct
        => ScheduleCore(graph, in context, out run, cancellationToken);

    private CommandEnqueueStatus ScheduleCore<TContext>(JobGraph<TContext> graph, in TContext context, out JobRun? run, CancellationToken token)
        where TContext : struct
    {
        VerifyOutsideHandler();
        run = null;
        ArgumentNullException.ThrowIfNull(graph);

        if (graph.Nodes[0].Runtime != this)
        {
            throw new ArgumentException("Graph belongs to another runtime.", nameof(graph));
        }

        lock (Sync)
        {
            CommandEnqueueStatus admission = GetNormalAdmissionStatus(token);
            if (admission != CommandEnqueueStatus.Accepted)
            {
                return admission;
            }
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
                CommandEnqueueStatus admission = GetNormalAdmissionStatus(token);
                if (admission != CommandEnqueueStatus.Accepted)
                {
                    return admission;
                }

                if (!Reserve(1, commands, bytes, false, units))
                {
                    run = null;
                    return CommandEnqueueStatus.QueueFull;
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

                if (_pending++ == 0)
                {
                    _quiet = null;
                }

                Notify();
                return CommandEnqueueStatus.Accepted;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(lengths, true);
        }
    }

    private bool PumpGraphPropagation()
    {
        for (int i = 0; i < _graphs.Count; i++)
        {
            int index = _graphCursor++ % _graphs.Count;
            _graphCursor %= _graphs.Count;
            GraphState state = _graphs[index];
            if (state.HasPropagation)
            {
                if (state.Step())
                {
                    _graphs.RemoveAt(index);
                    _graphCursor = 0;
                    FinishPending();
                }

                return true;
            }
        }

        return false;
    }
}
