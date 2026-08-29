namespace Lumyte.Interaction;

public sealed record ActionBindingConflict(
    ActionBindingSlot Slot,
    ActionBindingSlot ConflictingSlot,
    InputControlDescriptor Control);
