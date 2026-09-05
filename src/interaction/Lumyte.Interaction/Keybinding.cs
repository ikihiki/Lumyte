namespace Lumyte.Interaction;

public sealed record Keybinding(Command Command, KeyChord Chord)
{
    public ContextCondition When { get; init; } = ContextCondition.Always;

    public int Priority { get; init; }
}
