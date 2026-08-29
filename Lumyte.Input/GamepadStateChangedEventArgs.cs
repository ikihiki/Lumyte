namespace Lumyte.Input;

public sealed class GamepadStateChangedEventArgs(GamepadState previous, GamepadState current) : EventArgs
{
    public GamepadState Previous { get; } = previous;

    public GamepadState Current { get; } = current;
}
