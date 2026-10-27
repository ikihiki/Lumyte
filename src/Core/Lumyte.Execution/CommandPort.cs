using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

/// <summary>A fixed, typed command endpoint connected once by runtime preparation.</summary>
/// <typeparam name="TCommand">The command payload type.</typeparam>
/// <typeparam name="TResult">The handler return type.</typeparam>
public sealed class CommandPort<TCommand, TResult> : ICommandEndpoint
    where TCommand : struct
{
    private WorkDefinition<TCommand, TResult>? _work;
    private bool _hasConnected;

    /// <inheritdoc/>
    bool ICommandEndpoint.HasConnected => _hasConnected;

    internal WorkDefinition<TCommand, TResult>? Work => Volatile.Read(ref _work);

    /// <summary>Attempts atomic admission without a batch, delegate, or task wrapper.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="completion">The single-use result source, only on acceptance.</param>
    /// <param name="cancellationToken">Cancellation before handler start.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus Enqueue(in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default)
    {
        ExecutionRuntime.VerifyCanSubmit();
        WorkDefinition<TCommand, TResult>? work = Work;
        if (work is null)
        {
            completion = null;
            return CommandEnqueueStatus.NotRunning;
        }

        return work.Module.Runtime.Enqueue(work, in command, cancellationToken, null, out completion);
    }

    /// <summary>Submits one command and returns its actual execution result.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="cancellationToken">Cancellation before handler start.</param>
    /// <returns>A result which must be observed once.</returns>
    public ValueTask<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        CommandEnqueueStatus status = Enqueue(in command, out IValueTaskSource<TResult>? completion, cancellationToken);
        return status switch
        {
            CommandEnqueueStatus.Accepted => new ValueTask<TResult>(completion!, 0),
            CommandEnqueueStatus.Canceled => ValueTask.FromCanceled<TResult>(cancellationToken),
            CommandEnqueueStatus.NotRunning => ValueTask.FromException<TResult>(new InvalidOperationException("Command admission is closed.")),
            CommandEnqueueStatus.QueueFull => ValueTask.FromException<TResult>(new ExecutionQueueFullException("Command capacity is exhausted.")),
            _ => throw new InvalidOperationException("Unknown enqueue status."),
        };
    }

    /// <inheritdoc/>
    void ICommandEndpoint.Connect(WorkDefinition work)
    {
        _hasConnected = true;
        Volatile.Write(ref _work, (WorkDefinition<TCommand, TResult>)work);
    }

    /// <inheritdoc/>
    void ICommandEndpoint.Close() => Volatile.Write(ref _work, null);
}
