namespace Lumyte.Execution;

/// <summary>An opaque, runtime-specific update identity.</summary>
public readonly struct UpdateKey
{
    internal UpdateKey(UpdateDefinition definition)
    {
        Definition = definition;
    }

    internal UpdateDefinition? Definition { get; }
}
