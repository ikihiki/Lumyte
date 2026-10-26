namespace Lumyte.Execution;

/// <summary>Runs an asynchronous operation whose synchronous steps share the bounded CPU scheduler.</summary>
/// <typeparam name="TCommand">The operation input.</typeparam>
/// <typeparam name="TResult">The completed operation result.</typeparam>
/// <remarks>
/// Normal awaits return to the scheduler. ConfigureAwait(false), async void, and unawaited child work
/// are outside this contract. Handlers may submit and await individual ports on the same runtime;
/// batch and graph operations remain outside handlers. Handlers must not block a worker.
/// Cancellation after entry is cooperative, and completion always represents the actual operation.
/// </remarks>
public interface IWorkflowHandler<TCommand, TResult>
    where TCommand : struct
{
    /// <summary>Runs the operation, including any asynchronous child operations.</summary>
    /// <param name="command">The typed input.</param>
    /// <param name="cancellationToken">Cancellation observed cooperatively after entry.</param>
    /// <returns>The actual result, observed once by the runtime.</returns>
    ValueTask<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken);
}
