namespace Lumyte.Execution;

/// <summary>Single-producer typed staging, reusable after completed run disposal.</summary>
public sealed class CommandBatch : IDisposable
{
    private readonly int _maxCommands;
    private readonly List<BatchItem> _items = [];
    private bool _disposed;
    private long _generation;

    /// <summary>Initializes a new instance of the <see cref="CommandBatch"/> class.</summary>
    /// <param name="maxCommands">The positive staging limit.</param>
    public CommandBatch(int maxCommands)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCommands);
        _maxCommands = maxCommands;
    }

    internal IReadOnlyList<BatchItem> Items => _items;

    internal CommandRun? ActiveRun { get; set; }

    internal long Generation => _generation;

    /// <summary>Stages a typed input without sending it.</summary>
    /// <typeparam name="TCommand">The command payload type.</typeparam>
    /// <typeparam name="TResult">The handler return type.</typeparam>
    /// <param name="port">The fixed work endpoint.</param>
    /// <param name="command">The input.</param>
    /// <returns>The result key for the next accepted run.</returns>
    public CommandResultKey<TResult> Add<TCommand, TResult>(CommandPort<TCommand, TResult> port, in TCommand command)
        where TCommand : struct
    {
        ExecutionRuntime.VerifyOutsideHandler();
        CheckMutable();
        if (_items.Count == _maxCommands)
        {
            throw new ExecutionQueueFullException("Batch staging capacity is exhausted.");
        }

        WorkDefinition<TCommand, TResult> work = port.Work ?? throw new InvalidOperationException("Endpoint is not connected.");
        if (work.Admission == WorkAdmission.Startup || work.Module.Runtime.Status is ExecutionStatus.Quiescing or ExecutionStatus.Stopping or ExecutionStatus.Stopped)
        {
            throw new InvalidOperationException("Work cannot be staged for normal submission.");
        }

        _items.Add(new BatchItem<TCommand, TResult>(work, in command));
        return new CommandResultKey<TResult>(this, _generation, _items.Count - 1);
    }

    /// <summary>Clears staging after completion and run disposal, invalidating old result keys.</summary>
    public void Reset()
    {
        ExecutionRuntime.VerifyOutsideHandler();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ActiveRun is { IsDisposed: false })
        {
            throw new InvalidOperationException("Dispose the completed run before resetting the batch.");
        }

        foreach (BatchItem item in _items)
        {
            item.Clear();
        }

        _items.Clear();
        ActiveRun = null;
        _generation = checked(_generation + 1);
    }

    /// <summary>Releases staging; an accepted run must already have been completed and disposed.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Reset();
        _disposed = true;
    }

    internal void CheckMutable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ActiveRun is not null)
        {
            throw new InvalidOperationException("Accepted batch must be reset before reuse.");
        }
    }
}
