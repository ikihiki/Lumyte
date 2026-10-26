namespace Lumyte.Execution;

/// <summary>An opaque typed result identity belonging to one batch generation.</summary>
/// <typeparam name="TResult">The result type.</typeparam>
public readonly struct CommandResultKey<TResult>
{
    internal CommandResultKey(CommandBatch batch, long generation, int index)
    {
        Batch = batch;
        Generation = generation;
        Index = index;
    }

    internal CommandBatch? Batch { get; }

    internal long Generation { get; }

    internal int Index { get; }
}
