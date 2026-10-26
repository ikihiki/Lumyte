namespace Lumyte.Execution;

internal abstract class RangeDefinition<TContext>(ModuleDefinition module, string name)
    where TContext : struct
{
    internal ModuleDefinition Module { get; } = module;

    internal string Name { get; } = name;

    internal abstract int GetLength(in TContext context);

    internal abstract void Invoke(in TContext context, int first, int endExclusive);

    internal abstract void Clear();
}
