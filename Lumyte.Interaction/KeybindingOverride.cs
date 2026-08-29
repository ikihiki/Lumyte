namespace Lumyte.Interaction;

public sealed record KeybindingOverride(
    string CommandId,
    KeyChord? Chord,
    ContextCondition When,
    bool Remove);
