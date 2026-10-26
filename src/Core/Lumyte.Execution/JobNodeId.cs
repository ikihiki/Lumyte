namespace Lumyte.Execution;

/// <summary>A node identity belonging to one graph builder.</summary>
public readonly struct JobNodeId
{
    internal JobNodeId(object builder, int index)
    {
        Builder = builder;
        Index = index;
    }

    internal object? Builder { get; }

    internal int Index { get; }
}
