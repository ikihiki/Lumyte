namespace Lumyte.Input;

public sealed class KeyChangedEventArgs(Key key, bool isPressed, bool isRepeat) : EventArgs
{
    public Key Key { get; } = key;

    public bool IsPressed { get; } = isPressed;

    public bool IsRepeat { get; } = isRepeat;
}
