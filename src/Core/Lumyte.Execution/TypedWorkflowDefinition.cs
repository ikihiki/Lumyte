namespace Lumyte.Execution;

internal sealed class TypedWorkflowDefinition<TCommand, TResult, THandler>(ModuleDefinition module, string name, THandler handler, WorkflowPort<TCommand, TResult> port)
    : WorkflowDefinition<TCommand, TResult>(module, name)
    where TCommand : struct
    where THandler : IWorkflowHandler<TCommand, TResult>
{
    private THandler _handler = handler;

    internal override ICommandEndpoint Endpoint => port;

    internal override ValueTask<TResult> Invoke(TCommand command, CancellationToken token) => _handler.ExecuteAsync(command, token);

    internal override void Clear() => _handler = default!;
}
