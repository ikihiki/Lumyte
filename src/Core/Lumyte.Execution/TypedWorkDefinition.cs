namespace Lumyte.Execution;

internal sealed class TypedWorkDefinition<TCommand, TResult, THandler>(ModuleDefinition module, string name, WorkOptions options, GateDefinition[] gates, THandler handler, CommandPort<TCommand, TResult> port)
    : WorkDefinition<TCommand, TResult>(module, name, options, gates)
    where TCommand : struct
    where THandler : ICommandHandler<TCommand, TResult>
{
    private THandler _handler = handler;

    internal override ICommandEndpoint Endpoint => port;

    internal override TResult Invoke(in TCommand command, in WorkContext context) => _handler.Execute(in command, in context);

    internal override void Clear() => _handler = default!;
}
