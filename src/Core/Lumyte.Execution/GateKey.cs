namespace Lumyte.Execution;

/// <summary>An opaque, runtime-specific gate identity.</summary>
public readonly struct GateKey
{
    internal GateKey(GateDefinition definition)
    {
        Definition = definition;
    }

    internal GateDefinition? Definition { get; }
}
