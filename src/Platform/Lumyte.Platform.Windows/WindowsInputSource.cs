using System.Numerics;
using System.Runtime.Versioning;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Internal;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>Acquires logical keyboard and mouse input from the platform's single event pump.</summary>
/// <remarks>Text and IME input belong to the text service. This source does not acquire gamepads, touch or pen.</remarks>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsInputSource : IInputSource
{
    private readonly WindowsPlatform _platform;
    private BufferedDevice? _keyboard;
    private BufferedDevice? _mouse;
    private bool _active;
    private bool _disposed;

    internal WindowsInputSource(WindowsPlatform platform)
    {
        _platform = platform;
    }

    /// <inheritdoc/>
    public void Initialize(IInputDeviceRegistry registry)
    {
        Check();
        ArgumentNullException.ThrowIfNull(registry);
        if (_active)
        {
            throw new InvalidOperationException("Source is already initialized.");
        }

        var keyboard = new BufferedDevice(_platform, new InputDeviceDescriptor(InputDeviceKind.Keyboard, "Windows keyboard", InputDeviceIdentityKind.Logical));
        var mouse = new BufferedDevice(_platform, new InputDeviceDescriptor(InputDeviceKind.Mouse, "Windows mouse", InputDeviceIdentityKind.Logical));
        InputDeviceId keyboardId;
        try
        {
            keyboardId = registry.RegisterDevice(keyboard);
        }
        catch
        {
            keyboard.Dispose();
            mouse.Dispose();
            throw;
        }

        try
        {
            registry.RegisterDevice(mouse);
        }
        catch
        {
            mouse.Dispose();
            registry.UnregisterDevice(keyboardId);
            throw;
        }

        _keyboard = keyboard;
        _mouse = mouse;
        _active = true;
        var focus = new FocusData(_platform.Windows.Any(window => window.State.IsActive));
        keyboard.Add(focus);
        mouse.Add(focus);
    }

    /// <inheritdoc/>
    public void Update() => Check();

    /// <inheritdoc/>
    public void Shutdown()
    {
        Check();
        _active = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        if (_active)
        {
            throw new InvalidOperationException("Shut down the consumer before disposing its source.");
        }

        _disposed = true;
        _platform.Release(this);
    }

    internal void Receive(WindowsWindow window, uint message, nuint wParam, nint lParam)
    {
        if (!_active || window.State.Lifetime != WindowLifetime.Open)
        {
            return;
        }

        if (message is 0x100 or 0x101 or 0x104 or 0x105)
        {
            int scan = (int)((lParam >> 16) & 0xFF);
            bool extended = (lParam & 0x01000000) != 0;
            Key key = PhysicalKeys.FromPcScan(scan, extended);
            bool down = message is 0x100 or 0x104;
            _keyboard!.Add(new KeyData(key, down, down && (lParam & 0x40000000) != 0));
        }
        else if (message is 0x7 or 0x8)
        {
            var data = new FocusData(message == 0x7);
            _keyboard!.Add(data);
            _mouse!.Add(data);
        }
        else if (message == 0x200)
        {
            float scale = window.State.Scale.X;
            _mouse!.Add(new MouseMoveData(new Vector2((short)(lParam & 0xFFFF) / scale, (short)((lParam >> 16) & 0xFFFF) / scale)));
        }
        else if (message is 0x20A or 0x20E)
        {
            float steps = (short)((wParam >> 16) & 0xFFFF) / 120f;
            _mouse!.Add(new MouseWheelData(message == 0x20A ? new Vector2(0, steps) : new Vector2(steps, 0)));
        }
        else if (message is 0x201 or 0x202 or 0x203 or 0x204 or 0x205 or 0x206 or 0x207 or 0x208 or 0x209 or 0x20B or 0x20C or 0x20D)
        {
            MouseButton button = message switch
            {
                0x201 or 0x202 or 0x203 => MouseButton.Left,
                0x204 or 0x205 or 0x206 => MouseButton.Right,
                0x207 or 0x208 or 0x209 => MouseButton.Middle,
                _ => (wParam >> 16) == 1 ? MouseButton.X1 : MouseButton.X2,
            };
            _mouse!.Add(new MouseButtonData(button, message is not (0x202 or 0x205 or 0x208 or 0x20C)));
        }
    }

    internal void Deactivate()
    {
        if (_active)
        {
            var data = new FocusData(false);
            _keyboard!.Add(data);
            _mouse!.Add(data);
        }
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.CheckMutation();
    }

    private sealed class BufferedDevice(WindowsPlatform platform, InputDeviceDescriptor descriptor) : IInputDevice
    {
        private readonly List<InputData> _pending = [];
        private bool _disposed;

        public InputDeviceDescriptor Descriptor { get; } = descriptor;

        public IReadOnlyList<InputData> DrainEvents()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            platform.CheckMutation();
            InputData[] batch = _pending.ToArray();
            IReadOnlyList<InputData> result = Array.AsReadOnly(batch);
            _pending.Clear();
            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            platform.CheckMutation();
            _pending.Clear();
            _disposed = true;
        }

        internal void Add(InputData data)
        {
            if (!_disposed)
            {
                _pending.Add(data);
            }
        }
    }
}
