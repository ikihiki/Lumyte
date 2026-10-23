namespace Lumyte.Execution;

internal sealed class TypedUpdateDefinition<THandler>(ModuleDefinition module, string name, OwnerDefinition owner, GateDefinition[] gates, THandler handler)
    : UpdateDefinition(module, name, owner, gates)
    where THandler : IUpdateHandler
{
    private THandler _handler = handler;

    internal override void Invoke(in UpdateTick tick, in WorkContext context) => _handler.Update(in tick, in context);

    internal override void Clear() => _handler = default!;
}
