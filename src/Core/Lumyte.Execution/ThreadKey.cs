namespace Lumyte.Execution;

/// <summary>An opaque, runtime-specific thread identity.</summary>
public readonly struct ThreadKey
{
    internal ThreadKey(ThreadDefinition definition)
    {
        Definition = definition;
    }

    internal ThreadDefinition? Definition { get; }
}
