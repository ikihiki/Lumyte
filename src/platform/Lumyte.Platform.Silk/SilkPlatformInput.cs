using Lumyte.Input;
using Lumyte.Platform;

namespace Lumyte.Platform.SilkNet;

public sealed class SilkPlatformInput : IPlatformInput, IDisposable
{
    private readonly List<SilkWindowInput> windows = [];
    private readonly Dictionary<GamepadId, SilkGamepad> gamepads = [];

    public event EventHandler<WindowInputChangedEventArgs>? WindowChanged;

    public event EventHandler<GamepadConnectionChangedEventArgs>? GamepadConnectionChanged;

    public IReadOnlyList<SilkWindowInput> Windows => windows;

    IReadOnlyList<IWindowInput> IPlatformInput.Windows => windows;

    public IReadOnlyList<SilkGamepad> Gamepads => [.. gamepads.Values];

    IReadOnlyList<IGamepad> IPlatformInput.Gamepads => Gamepads;

    public SilkWindowInput GetWindow(SilkWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return windows.Single(input => ReferenceEquals(input.Window, window));
    }

    IWindowInput IPlatformInput.GetWindow(IWindow window) => window is SilkWindow silkWindow
        ? GetWindow(silkWindow)
        : throw new ArgumentException("The window does not belong to this Silk platform.", nameof(window));

    public void Dispose()
    {
        foreach (SilkWindowInput window in windows.ToArray())
        {
            Remove(window);
        }
    }

    internal void Add(SilkWindowInput input)
    {
        windows.Add(input);
        foreach (SilkGamepad gamepad in input.Gamepads)
        {
            AddGamepad(gamepad);
        }

        input.GamepadConnectionChanged += OnGamepadConnectionChanged;
        WindowChanged?.Invoke(this, new(input, true));
    }

    internal void Remove(SilkWindowInput input)
    {
        if (!windows.Remove(input))
        {
            return;
        }

        input.GamepadConnectionChanged -= OnGamepadConnectionChanged;
        WindowChanged?.Invoke(this, new(input, false));
        foreach (SilkGamepad gamepad in input.Gamepads)
        {
            RemoveGamepad(gamepad);
        }

        input.Dispose();
    }

    private void OnGamepadConnectionChanged(
        object? sender,
        GamepadConnectionChangedEventArgs eventArgs)
    {
        var gamepad = (SilkGamepad)eventArgs.Gamepad;
        if (eventArgs.IsConnected)
        {
            AddGamepad(gamepad);
        }
        else
        {
            RemoveGamepad(gamepad);
        }
    }

    private void AddGamepad(SilkGamepad gamepad)
    {
        if (gamepads.TryAdd(gamepad.Id, gamepad))
        {
            GamepadConnectionChanged?.Invoke(this, new(gamepad, true));
        }
    }

    private void RemoveGamepad(SilkGamepad gamepad)
    {
        if (!gamepads.TryGetValue(gamepad.Id, out SilkGamepad? registered)
            || !ReferenceEquals(registered, gamepad))
        {
            return;
        }

        SilkGamepad? replacement = windows
            .SelectMany(window => window.Gamepads)
            .FirstOrDefault(candidate =>
                candidate.Id == gamepad.Id
                && !ReferenceEquals(candidate, gamepad));
        if (replacement is not null)
        {
            gamepads[gamepad.Id] = replacement;
        }
        else if (gamepads.Remove(gamepad.Id))
        {
            GamepadConnectionChanged?.Invoke(this, new(gamepad, false));
        }
    }
}
