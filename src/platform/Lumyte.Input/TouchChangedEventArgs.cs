namespace Lumyte.Input;

public sealed class TouchChangedEventArgs(TouchPoint touch) : EventArgs
{
    public TouchPoint Touch { get; } = touch;
}
