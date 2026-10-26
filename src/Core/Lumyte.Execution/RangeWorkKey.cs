namespace Lumyte.Execution;

/// <summary>An opaque range definition.</summary>
/// <typeparam name="TContext">The shared graph input type.</typeparam>
public readonly struct RangeWorkKey<TContext>
    where TContext : struct
{
    internal RangeWorkKey(RangeDefinition<TContext> definition)
    {
        Definition = definition;
    }

    internal RangeDefinition<TContext>? Definition { get; }
}
