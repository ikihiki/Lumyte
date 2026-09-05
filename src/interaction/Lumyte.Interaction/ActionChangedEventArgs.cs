namespace Lumyte.Interaction;

public sealed class ActionChangedEventArgs(
    InputAction<bool> action,
    bool value) : EventArgs
{
    public InputAction<bool> Action { get; } = action;

    public bool Value { get; } = value;
}
