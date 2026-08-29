namespace Lumyte.Input;

public interface IGamepad
{
    event EventHandler<GamepadStateChangedEventArgs>? StateChanged;

    string Name { get; }

    GamepadState State { get; }

    bool SupportsVibration { get; }

    void SetVibration(GamepadVibration vibration);
}
