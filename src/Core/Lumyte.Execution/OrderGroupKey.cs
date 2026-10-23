namespace Lumyte.Execution;

/// <summary>An opaque, runtime-specific ordergroup identity.</summary>
public readonly struct OrderGroupKey
{
    internal OrderGroupKey(GroupDefinition definition)
    {
        Definition = definition;
    }

    internal GroupDefinition? Definition { get; }
}
