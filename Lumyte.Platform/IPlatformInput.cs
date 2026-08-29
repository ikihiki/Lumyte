using Lumyte.Input;

namespace Lumyte.Platform;

public interface IPlatformInput
{
    event EventHandler<WindowInputChangedEventArgs>? WindowChanged;

    event EventHandler<GamepadConnectionChangedEventArgs>? GamepadConnectionChanged;

    IReadOnlyList<IWindowInput> Windows { get; }

    IReadOnlyList<IGamepad> Gamepads { get; }

    IWindowInput GetWindow(IWindow window);
}
