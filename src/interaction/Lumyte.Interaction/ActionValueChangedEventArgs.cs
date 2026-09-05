namespace Lumyte.Interaction;

public sealed class ActionValueChangedEventArgs(
    InteractionIntent action,
    object? previousValue,
    object? value) : EventArgs
{
    public InteractionIntent Action { get; } = action;

    public object? PreviousValue { get; } = previousValue;

    public object? Value { get; } = value;
}
