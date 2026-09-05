namespace Lumyte.Interaction;

public sealed record ActionRuntimeSnapshot(IReadOnlyList<ActionMapSnapshot> Maps, IReadOnlyList<ActionStateSnapshot> Actions);
public sealed record ActionMapSnapshot(string Name, int Priority, IReadOnlyList<ActionBindingSnapshot> Bindings);
public sealed record ActionBindingSnapshot(string ActionId, string? BindingId, string Control, string ValueType);
public sealed record ActionStateSnapshot(string Id, string ValueType, object? Value, ActionPhase Phase);
