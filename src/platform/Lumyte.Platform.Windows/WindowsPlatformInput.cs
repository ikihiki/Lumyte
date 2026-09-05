using System.Diagnostics;

using Lumyte.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.XboxController;

namespace Lumyte.Platform.Windows;

public sealed class WindowsPlatformInput : IPlatformInput
{
    private const int MaximumGamepadCount = 4;
    private static readonly long s_discoveryInterval = Stopwatch.Frequency * 2;
    private readonly List<WindowsWindowInput> windows = [];
    private readonly List<WindowsGamepad> gamepads = [];
    private readonly WindowsGamepad?[] gamepadSlots = new WindowsGamepad?[MaximumGamepadCount];
    private long nextDiscoveryTimestamp;

    public event EventHandler<WindowInputChangedEventArgs>? WindowChanged;

    public event EventHandler<GamepadConnectionChangedEventArgs>? GamepadConnectionChanged;

    public IReadOnlyList<WindowsWindowInput> Windows => windows;

    IReadOnlyList<IWindowInput> IPlatformInput.Windows => windows;

    public IReadOnlyList<WindowsGamepad> Gamepads => gamepads;

    IReadOnlyList<IGamepad> IPlatformInput.Gamepads => gamepads;

    public WindowsWindowInput GetWindow(WindowsWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return windows.Single(input => ReferenceEquals(input.Window, window));
    }

    IWindowInput IPlatformInput.GetWindow(IWindow window) => window is WindowsWindow windowsWindow
        ? GetWindow(windowsWindow)
        : throw new ArgumentException("The window does not belong to this Windows platform.", nameof(window));

    internal void Add(WindowsWindowInput input)
    {
        windows.Add(input);
        WindowChanged?.Invoke(this, new(input, true));
    }

    internal void Remove(WindowsWindowInput input)
    {
        if (windows.Remove(input))
        {
            WindowChanged?.Invoke(this, new(input, false));
            input.Dispose();
        }
    }

    internal void Update()
    {
        long timestamp = Stopwatch.GetTimestamp();
        bool discover = timestamp >= nextDiscoveryTimestamp;
        for (uint index = 0; index < MaximumGamepadCount; index++)
        {
            WindowsGamepad? gamepad = gamepadSlots[index];
            if (gamepad is null && !discover)
            {
                continue;
            }

            bool isConnected = PInvoke.XInputGetState(index, out XINPUT_STATE nativeState)
                == 0;

            if (isConnected)
            {
                if (gamepad is null)
                {
                    gamepad = new(index);
                    gamepadSlots[index] = gamepad;
                    gamepads.Add(gamepad);
                    GamepadConnectionChanged?.Invoke(this, new(gamepad, true));
                }

                gamepad.Update(WindowsGamepad.Convert(nativeState));
            }
            else if (gamepad is not null)
            {
                gamepadSlots[index] = null;
                gamepads.Remove(gamepad);
                GamepadConnectionChanged?.Invoke(this, new(gamepad, false));
            }
        }

        if (discover)
        {
            nextDiscoveryTimestamp = timestamp + s_discoveryInterval;
        }
    }
}
