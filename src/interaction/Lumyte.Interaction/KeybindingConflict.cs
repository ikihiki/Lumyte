namespace Lumyte.Interaction;

public sealed record KeybindingConflict(IReadOnlyList<Keybinding> Bindings);
