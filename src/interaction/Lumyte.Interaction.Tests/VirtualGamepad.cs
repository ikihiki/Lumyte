using Lumyte.Input;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualGamepad(
    string name = "Virtual gamepad",
    string? id = null) : IGamepad
{
    public event EventHandler<GamepadStateChangedEventArgs>? StateChanged;

    public GamepadId Id { get; } = new(id ?? $"virtual:{name}");

    public string Name { get; } = name;

    public GamepadState State { get; private set; }

    public bool SupportsVibration => false;

    public void SetVibration(GamepadVibration vibration)
    {
    }

    public void SetState(GamepadState state)
    {
        GamepadState previous = State;
        State = state;
        StateChanged?.Invoke(this, new(previous, state));
    }
}
