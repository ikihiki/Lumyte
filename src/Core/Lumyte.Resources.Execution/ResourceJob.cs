using System.Threading.Tasks.Sources;
using Lumyte.Execution;

namespace Lumyte.Resources.Execution;

/// <summary>Declares typed CPU work shared by initial loads and resource rebuilds.</summary>
/// <typeparam name="TCommand">The detached job payload.</typeparam>
/// <typeparam name="TResult">The completed result.</typeparam>
/// <typeparam name="THandler">The stateless synchronous CPU handler.</typeparam>
public sealed class ResourceJob<TCommand, TResult, THandler> : IExecutionModule
    where TCommand : struct
    where THandler : struct, ICommandHandler<TCommand, TResult>
{
    private static readonly CommandPort<TCommand, TResult> _port = new();

    /// <inheritdoc/>
    public string Name => $"ResourceJob:{typeof(TCommand).FullName}:{typeof(TResult).FullName}:{typeof(THandler).FullName}";

    /// <summary>Submits CPU work and awaits its actual result without creating a task or closure.</summary>
    /// <param name="command">The input, retaining any buffers until execution finishes.</param>
    /// <param name="cancellationToken">Cancels admission or work that has not started.</param>
    /// <returns>The result; cancellation never abandons a running handler's owned output.</returns>
    public ValueTask<TResult> Execute(TCommand command, CancellationToken cancellationToken = default)
    {
        CommandEnqueueStatus status = _port.TryEnqueue(in command, cancellationToken, out IValueTaskSource<TResult>? completion);
        return status switch
        {
            CommandEnqueueStatus.Accepted => new ValueTask<TResult>(completion!, 0),
            CommandEnqueueStatus.Canceled => ValueTask.FromCanceled<TResult>(cancellationToken),
            CommandEnqueueStatus.NotRunning => ValueTask.FromException<TResult>(new InvalidOperationException("Resource job admission is closed.")),
            CommandEnqueueStatus.QueueFull => ValueTask.FromException<TResult>(new ExecutionQueueFullException("Resource job capacity is exhausted.")),
            _ => throw new InvalidOperationException("Unknown command admission status."),
        };
    }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
        => builder.Command<TCommand, TResult, THandler>("Execute", default, new WorkOptions { Target = WorkTarget.Cpu }, _port);

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
    }

    /// <inheritdoc/>
    public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

    /// <inheritdoc/>
    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;
}
