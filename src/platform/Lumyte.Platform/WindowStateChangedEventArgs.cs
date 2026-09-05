namespace Lumyte.Platform;

public sealed class WindowStateChangedEventArgs(WindowState state) : EventArgs
{
    public WindowState State { get; } = state;
}
