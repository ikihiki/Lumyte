namespace Lumyte.Interaction;

public sealed class ActionPhaseChangedEventArgs(
    InteractionIntent action,
    ActionPhase phase,
    object? value) : EventArgs
{
    public InteractionIntent Action { get; } = action;

    public ActionPhase Phase { get; } = phase;

    public object? Value { get; } = value;
}
