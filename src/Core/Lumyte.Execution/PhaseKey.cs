namespace Lumyte.Execution;

/// <summary>An opaque, runtime-specific phase identity.</summary>
public readonly struct PhaseKey
{
    internal PhaseKey(PhaseDefinition definition)
    {
        Definition = definition;
    }

    internal PhaseDefinition? Definition { get; }
}
