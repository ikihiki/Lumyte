namespace Lumyte.Input;

public sealed class GamepadConnectionChangedEventArgs(IGamepad gamepad, bool isConnected) : EventArgs
{
    public IGamepad Gamepad { get; } = gamepad;

    public bool IsConnected { get; } = isConnected;
}
