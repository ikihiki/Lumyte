using System.Numerics;
using System.Runtime.Versioning;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Internal;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows;

/// <summary>Acquires one ordered keyboard/mouse stream with native window identity and mouse capture.</summary>
[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsInputSource : IWindowInputSource
{
    private readonly BufferedInputSource _buffer;
    private readonly HashSet<MouseButton> _buttons = [];
    private readonly HashSet<Key> _physicalKeys = [];
    private readonly Dictionary<Key, WindowId> _publishedKeys = [];
    private readonly HashSet<Key> _textConsumedKeys = [];
    private readonly Dictionary<Key, int> _virtualKeys = [];
    private WindowsWindow? _captureWindow;
    private nint _captureHandle;
    private bool _blocked;

    internal WindowsInputSource(WindowsPlatform platform)
    {
        _buffer = new(platform.CheckMutation, _ => platform.Release(this), () => platform.Windows, "Windows");
    }

    /// <inheritdoc/>
    public Guid StreamId => _buffer.StreamId;

    internal IReadOnlyCollection<Key> HeldKeys => _physicalKeys;

    /// <inheritdoc/>
    public void Initialize(IInputDeviceRegistry registry) => _buffer.Initialize(registry);

    /// <inheritdoc/>
    public void Update()
    {
        _buffer.Update();
        ReconcilePhysicalKeys();
        if (_blocked && new[] { 1, 2, 4, 5, 6, }.All(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) == 0))
        {
            _buttons.Clear();
            _blocked = false;
        }
    }

    /// <inheritdoc/>
    public InputEventBatch? PeekEvents() => _buffer.PeekEvents();

    /// <inheritdoc/>
    public void CommitEvents(ulong batchId) => _buffer.CommitEvents(batchId);

    /// <inheritdoc/>
    public bool TryGetInputWindowId(WindowId windowId, out InputWindowId inputWindowId) => _buffer.TryGetInputWindowId(windowId, out inputWindowId);

    /// <inheritdoc/>
    public void AbortEvents()
    {
        CancelCapture();
        _buffer.AbortEvents();
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        CancelCapture();
        _buffer.Shutdown();
    }

    /// <inheritdoc/>
    public void Dispose() => _buffer.Dispose();

    internal InputCaptureStamp BeginKeyAcquisition(nuint virtualKey, nint lParam, bool down)
    {
        Key key = PhysicalKeys.FromPcScan((int)((lParam >> 16) & 0xFF), (lParam & 0x01000000) != 0);
        if (down)
        {
            _physicalKeys.Add(key);
            _virtualKeys[key] = (int)virtualKey;
        }
        else
        {
            _physicalKeys.Remove(key);
            _virtualKeys.Remove(key);
        }

        return new(StreamId, _buffer.BeginAcquisition());
    }

    internal void ReconcilePhysicalKeys()
    {
        List<Key>? released = null;
        foreach ((Key key, int virtualKey) in _virtualKeys)
        {
            if ((NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) == 0)
            {
                (released ??= []).Add(key);
            }
        }

        if (released is null)
        {
            return;
        }

        ulong capture = _buffer.BeginAcquisition();
        foreach (Key key in released)
        {
            _physicalKeys.Remove(key);
            _virtualKeys.Remove(key);
            _textConsumedKeys.Remove(key);
            if (_publishedKeys.Remove(key, out WindowId owner))
            {
                _buffer.Keyboard(owner, new KeyCanceledData(key), capture);
            }
        }
    }

    internal void ConsumeKey(WindowsWindow window, nint lParam, InputCaptureStamp capture)
    {
        Key key = PhysicalKeys.FromPcScan((int)((lParam >> 16) & 0xFF), (lParam & 0x01000000) != 0);
        if (_physicalKeys.Contains(key))
        {
            _textConsumedKeys.Add(key);
        }
        else
        {
            _textConsumedKeys.Remove(key);
        }

        if (_publishedKeys.Remove(key, out WindowId owner))
        {
            _buffer.Keyboard(owner, new KeyCanceledData(key), capture.Sequence);
        }
    }

    internal void BeginTextScope()
    {
        ulong capture = _buffer.BeginAcquisition();
        foreach ((Key key, WindowId owner) in _publishedKeys)
        {
            _buffer.Keyboard(owner, new KeyCanceledData(key), capture);
        }

        _publishedKeys.Clear();
        _textConsumedKeys.UnionWith(_physicalKeys);
    }

    internal void Receive(WindowsWindow window, uint message, nuint wParam, nint lParam, ulong? keyCapture = null)
    {
        if (message is 0x215 or 0x1F)
        {
            if (_captureWindow == window)
            {
                CancelCapture();
            }

            return;
        }

        if (_captureWindow == window && (!window.State.IsVisible || window.State.Lifetime != WindowLifetime.Open))
        {
            CancelCapture();
        }

        if (window.State.Lifetime != WindowLifetime.Open)
        {
            _buffer.Close(window.Id);
            return;
        }

        if (message is 0x100 or 0x101 or 0x104 or 0x105)
        {
            Key key = PhysicalKeys.FromPcScan((int)((lParam >> 16) & 0xFF), (lParam & 0x01000000) != 0);
            bool down = message is 0x100 or 0x104;
            if (keyCapture is null)
            {
                keyCapture = BeginKeyAcquisition(wParam, lParam, down).Sequence;
            }

            if (_textConsumedKeys.Contains(key))
            {
                if (!down)
                {
                    _textConsumedKeys.Remove(key);
                }

                return;
            }

            if (down && (lParam & 0x40000000) != 0 && !_publishedKeys.ContainsKey(key))
            {
                return;
            }

            if (down)
            {
                _publishedKeys.TryAdd(key, window.Id);
            }
            else if (!_publishedKeys.Remove(key))
            {
                return;
            }

            _buffer.Keyboard(window.Id, new KeyData(key, down, down && (lParam & 0x40000000) != 0), keyCapture);
        }
        else if (message is 0x7 or 0x8)
        {
            if (message == 0x8 && _captureWindow == window)
            {
                CancelCapture();
            }

            _buffer.Focus(window.Id, message == 0x7);
        }
        else if (message == 0x200 && !_blocked)
        {
            _buffer.Mouse(window.Id, new MouseMoveData(Position(window, lParam)));
        }
        else if (message is 0x20A or 0x20E)
        {
            float steps = (short)((wParam >> 16) & 0xFFFF) / 120f;
            _buffer.Mouse(window.Id, new MouseWheelData(message == 0x20A ? new Vector2(0, steps) : new Vector2(steps, 0)));
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
            bool down = message is not (0x202 or 0x205 or 0x208 or 0x20C);
            if (down)
            {
                if (_buttons.Count == 0 && !_blocked)
                {
                    NativeMethods.SetCapture(window.Hwnd);
                    if (NativeMethods.GetCapture() == window.Hwnd)
                    {
                        _captureWindow = window;
                        _captureHandle = window.Hwnd;
                    }
                    else
                    {
                        _blocked = true;
                    }
                }

                _buttons.Add(button);
            }
            else
            {
                _buttons.Remove(button);
            }

            try
            {
                if (!_blocked)
                {
                    ulong capture = _buffer.BeginAcquisition();
                    _buffer.Mouse(window.Id, new MouseMoveData(Position(window, lParam)), capture);
                    _buffer.Mouse(window.Id, new MouseButtonData(button, down), capture);
                }
            }
            finally
            {
                if (_buttons.Count == 0)
                {
                    nint handle = _captureHandle;
                    _captureHandle = 0;
                    _captureWindow = null;
                    _blocked = false;
                    if (handle != 0 && NativeMethods.GetCapture() == handle)
                    {
                        NativeMethods.ReleaseCapture();
                    }
                }
            }
        }
    }

    internal void Close(WindowsWindow window)
    {
        if (_captureWindow == window)
        {
            CancelCapture();
        }

        _buffer.Close(window.Id);
    }

    private static Vector2 Position(WindowsWindow window, nint packed) => new Vector2((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF)) / window.State.Scale;

    private void CancelCapture()
    {
        WindowsWindow? owner = _captureWindow;
        nint handle = _captureHandle;
        _captureHandle = 0;
        _captureWindow = null;
        _blocked = _buttons.Count != 0;
        if (owner is null)
        {
            return;
        }

        foreach (MouseButton button in _buttons)
        {
            _buffer.Mouse(owner.Id, new MouseButtonCanceledData(button));
        }

        if (handle != 0 && NativeMethods.GetCapture() == handle)
        {
            NativeMethods.ReleaseCapture();
        }
    }
}
