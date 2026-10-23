namespace Lumyte.Execution;

internal sealed class TypedRangeDefinition<TContext, THandler>(ModuleDefinition module, string name, THandler handler)
    : RangeDefinition<TContext>(module, name)
    where TContext : struct
    where THandler : IRangeWorkHandler<TContext>
{
    private THandler _handler = handler;

    internal override int GetLength(in TContext context) => _handler.GetLength(in context);

    internal override void Invoke(in TContext context, int first, int endExclusive) => _handler.Execute(in context, first, endExclusive);

    internal override void Clear() => _handler = default!;
}
