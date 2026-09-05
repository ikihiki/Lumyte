namespace Lumyte.Platform;

public sealed class WindowFocusChangedEventArgs(bool isFocused) : EventArgs
{
    public bool IsFocused { get; } = isFocused;
}
