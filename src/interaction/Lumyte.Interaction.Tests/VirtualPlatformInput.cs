using Lumyte.Input;
using Lumyte.Platform;

namespace Lumyte.Interaction.Tests;

internal sealed class VirtualPlatformInput : IPlatformInput
{
    private readonly List<IGamepad> gamepads = [];

    public event EventHandler<WindowInputChangedEventArgs>? WindowChanged
    {
        add { }
        remove { }
    }

    public event EventHandler<GamepadConnectionChangedEventArgs>? GamepadConnectionChanged;

    public IReadOnlyList<IWindowInput> Windows => [];

    public IReadOnlyList<IGamepad> Gamepads => gamepads;

    public IWindowInput GetWindow(IWindow window) =>
        throw new ArgumentException("The virtual platform has no windows.", nameof(window));

    public void Connect(IGamepad gamepad)
    {
        gamepads.Add(gamepad);
        GamepadConnectionChanged?.Invoke(this, new(gamepad, true));
    }

    public void Disconnect(IGamepad gamepad)
    {
        gamepads.Remove(gamepad);
        GamepadConnectionChanged?.Invoke(this, new(gamepad, false));
    }
}
