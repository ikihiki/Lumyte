namespace Lumyte.Interaction;

public sealed class ContextValueChangedEventArgs(
    ContextKey key,
    object? previousValue,
    object? value) : EventArgs
{
    public ContextKey Key { get; } = key;

    public object? PreviousValue { get; } = previousValue;

    public object? Value { get; } = value;
}
