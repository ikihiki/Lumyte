using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

/// <summary>A fixed endpoint for asynchronous CPU workflows, connected once during preparation.</summary>
/// <typeparam name="TCommand">The operation input.</typeparam>
/// <typeparam name="TResult">The operation result.</typeparam>
public sealed class WorkflowPort<TCommand, TResult> : ICommandEndpoint
    where TCommand : struct
{
    private WorkflowDefinition<TCommand, TResult>? _work;
    private bool _hasConnected;

    /// <inheritdoc/>
    bool ICommandEndpoint.HasConnected => _hasConnected;

    /// <summary>Reserves one operation until its actual result is observed.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="token">Cancellation before entry and cooperative cancellation thereafter.</param>
    /// <param name="completion">The single-use result source, only on acceptance.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus TryEnqueue(in TCommand command, CancellationToken token, out IValueTaskSource<TResult>? completion)
    {
        ExecutionRuntime.VerifyCanSubmit();
        WorkflowDefinition<TCommand, TResult>? work = Volatile.Read(ref _work);
        if (work is null)
        {
            completion = null;
            return CommandEnqueueStatus.NotRunning;
        }

        return work.Module.Runtime.EnqueueWorkflow(work, in command, token, out completion);
    }

    /// <summary>Attempts workflow admission with a detailed result and optional cancellation.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="completion">The single-use result source, only on acceptance.</param>
    /// <param name="cancellationToken">Cancellation before entry.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus Enqueue(in TCommand command, out IValueTaskSource<TResult>? completion, CancellationToken cancellationToken = default)
        => TryEnqueue(in command, cancellationToken, out completion);

    /// <summary>Submits an operation and returns its actual completion.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="token">Cancellation before entry and cooperative cancellation thereafter.</param>
    /// <returns>A result which must be observed once, including after cancellation.</returns>
    public ValueTask<TResult> Execute(TCommand command, CancellationToken token = default)
    {
        CommandEnqueueStatus status = TryEnqueue(in command, token, out IValueTaskSource<TResult>? completion);
        return status switch
        {
            CommandEnqueueStatus.Accepted => new ValueTask<TResult>(completion!, 0),
            CommandEnqueueStatus.Canceled => ValueTask.FromCanceled<TResult>(token),
            CommandEnqueueStatus.NotRunning => ValueTask.FromException<TResult>(new InvalidOperationException("Workflow admission is closed.")),
            CommandEnqueueStatus.QueueFull => ValueTask.FromException<TResult>(new ExecutionQueueFullException("Workflow capacity is exhausted.")),
            _ => throw new InvalidOperationException("Unknown enqueue status."),
        };
    }

    /// <summary>Submits a workflow and returns its actual completion.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="cancellationToken">Cancellation before entry and cooperative cancellation thereafter.</param>
    /// <returns>A result which must be observed once, including after cancellation.</returns>
    public ValueTask<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken = default)
        => Execute(command, cancellationToken);

    /// <inheritdoc/>
    void ICommandEndpoint.Connect(WorkDefinition work)
    {
        _hasConnected = true;
        Volatile.Write(ref _work, (WorkflowDefinition<TCommand, TResult>)work);
    }

    /// <inheritdoc/>
    void ICommandEndpoint.Close() => Volatile.Write(ref _work, null);
}
