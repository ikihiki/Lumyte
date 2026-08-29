namespace Lumyte.Interaction;

public sealed class KeybindingConflictEventArgs(
    IReadOnlyList<Keybinding> bindings) : EventArgs
{
    public IReadOnlyList<Keybinding> Bindings { get; } = bindings;
}
