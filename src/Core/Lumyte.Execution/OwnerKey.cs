namespace Lumyte.Execution;

/// <summary>An opaque, runtime-specific owner identity.</summary>
public readonly struct OwnerKey
{
    internal OwnerKey(OwnerDefinition definition)
    {
        Definition = definition;
    }

    internal OwnerDefinition? Definition { get; }
}
