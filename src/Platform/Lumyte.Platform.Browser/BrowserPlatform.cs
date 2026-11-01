using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.Internal;
using Lumyte.Windowing.Abstractions;

[assembly: SupportedOSPlatform("browser")]

namespace Lumyte.Platform.Browser;

/// <summary>Uses an application-provided HTML canvas as one native presentation target.</summary>
/// <remarks>Hosted menus require the optional Windowing library. Independent browser windows are unsupported.</remarks>
public sealed class BrowserPlatform : IPlatform
{
    private readonly HashSet<BrowserClipboard> _clipboards = [];
    private readonly JSObject _canvas;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Queue<Action> _operations = new();
    private readonly Queue<Action> _notifications = new();
    private readonly List<Exception> _errors = [];
    private readonly HashSet<Key> _publishedTextKeys = [];
    private BufferedInputSource? _source;
    private BrowserWindow? _window;
    private ulong _nextWindow;
    private bool _pumping;
    private bool _notifying;
    private bool _disposed;
    private bool _textInputRegistered;

    private BrowserPlatform(JSObject canvas)
    {
        _canvas = canvas;
    }

    /// <inheritdoc/>
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public PlatformFeatures Features => PlatformFeatures.NativeWindows | PlatformFeatures.DeviceInput | (BrowserInterop.HasClipboard() ? PlatformFeatures.Clipboard : PlatformFeatures.None);

    internal JSObject TextInputCanvas => _canvas;

    internal BrowserWindow? TextInputWindow => _window;

    /// <summary>Imports the JavaScript module and borrows an application-provided HTML canvas.</summary>
    /// <param name="canvas">An HTMLCanvasElement supporting WebGPU.</param>
    /// <param name="modulePath">The URL of the packaged JavaScript module.</param>
    /// <returns>A caller-owned platform. The canvas proxy remains owned by the caller.</returns>
    public static async Task<BrowserPlatform> CreateAsync(JSObject canvas, string modulePath = "./lumyte-windowing.js")
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location") ?? throw new InvalidOperationException("The browser has no location for resolving the module URL.");
        string baseUrl = location.GetPropertyAsString("href") ?? throw new InvalidOperationException("The browser location has no URL.");
        var resolved = new Uri(new Uri(baseUrl), modulePath);
        using JSObject module = await JSHost.ImportAsync("Lumyte.Platform.Browser", resolved.AbsoluteUri);
        return new BrowserPlatform(canvas);
    }

    /// <inheritdoc/>
    public IWindow CreateWindow(WindowDescriptor descriptor)
    {
        CheckMutation();
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_window is not null || descriptor.Owner is not null || descriptor.Kind != WindowKind.Normal)
        {
            throw new NotSupportedException("Only one existing canvas is supported. Use Hosted regions for child displays.");
        }

        BrowserWindow.Validate(descriptor);
        _window = new BrowserWindow(this, new WindowId(Id, checked(++_nextWindow)), _canvas, descriptor);
        return _window;
    }

    /// <inheritdoc/>
    public IInputSource CreateInputSource()
    {
        CheckMutation();
        if (_source is not null || _textInputRegistered)
        {
            throw new InvalidOperationException("Only one input source is supported.");
        }

        _window?.SetInputEnabled(false);
        _source = new BufferedInputSource(CheckMutation, _ => _source = null, () => _window is null ? [] : new IWindow[] { _window, }, "Browser", () => _window?.SetInputEnabled(false), () => _window?.SetInputEnabled(true));
        return _source;
    }

    /// <inheritdoc/>
    public IClipboard CreateClipboard()
    {
        CheckMutation();
        if (!BrowserInterop.HasClipboard())
        {
            throw new NotSupportedException("Clipboard access requires a secure browser context with Clipboard API support.");
        }

        var clipboard = new BrowserClipboard(this);
        _clipboards.Add(clipboard);
        return clipboard;
    }

    /// <inheritdoc/>
    public void PumpEvents()
    {
        CheckMutation();
        if (_pumping)
        {
            throw new InvalidOperationException("Reentrant pumping is forbidden.");
        }

        _pumping = true;
        try
        {
            while (_operations.TryDequeue(out Action? operation))
            {
                Execute(operation);
            }

            if (_window is { } window)
            {
                Execute(window.Observe);
            }

            _notifying = true;
            while (_notifications.TryDequeue(out Action? notification))
            {
                Execute(notification);
            }
        }
        finally
        {
            _notifying = false;
            _pumping = false;
        }

        if (_errors.Count > 0)
        {
            Exception[] errors = _errors.ToArray();
            _errors.Clear();
            throw new AggregateException(errors);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CheckMutation();
        if (_pumping || _window is not null || _clipboards.Count > 0 || _source is not null)
        {
            throw new InvalidOperationException("Dispose the canvas window before the platform.");
        }

        _disposed = true;
    }

    internal IDisposable RegisterTextInput()
    {
        CheckMutation();
        if (_textInputRegistered)
        {
            throw new InvalidOperationException("One textarea service is allowed per Browser platform.");
        }

        _textInputRegistered = true;
        return new TextInputRegistration(() => _textInputRegistered = false);
    }

    internal IDisposable RegisterTextProxy(JSObject proxy, string token)
    {
        Check();
        BrowserWindow window = _window ?? throw new InvalidOperationException("Create the canvas window before beginning text input.");
        BrowserInterop.RegisterTextProxy(window.TextInputHandle, proxy, token);
        return new TextInputRegistration(() => BrowserInterop.UnregisterTextProxy(window.TextInputHandle, token));
    }

    internal IDisposable RegisterTextFocusBridge(Func<bool> bridge)
    {
        Check();
        BrowserWindow window = _window ?? throw new InvalidOperationException("Create the canvas window before registering its focus bridge.");
        BrowserInterop.SetTextFocusBridge(window.TextInputHandle, bridge);
        return new TextInputRegistration(() => BrowserInterop.SetTextFocusBridge(window.TextInputHandle, null));
    }

    internal void BeginTextInputScope()
    {
        ulong capture = _source?.BeginAcquisition() ?? 0;
        foreach (Key key in _publishedTextKeys)
        {
            _source?.Keyboard(_window!.Id, new KeyCanceledData(key), capture);
        }

        _publishedTextKeys.Clear();
    }

    internal InputCaptureStamp ReserveTextInputCapture() => new(_source?.StreamId ?? Guid.Empty, _source?.BeginAcquisition() ?? 0);

    internal InputWindowId ResolveTextInputOrigin(WindowId id) => _source is not null && _source.TryGetInputWindowId(id, out InputWindowId origin) ? origin : throw new NotSupportedException("The text target has no acquisition origin.");

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Use the creating browser context.");
        }
    }

    internal void CheckMutation()
    {
        Check();
        if (_notifying)
        {
            throw new InvalidOperationException("Mutation during notifications is forbidden.");
        }
    }

    internal void Enqueue(Action operation) => _operations.Enqueue(operation);

    internal void StopInput(BrowserWindow window)
    {
        if (window.State.IsActive)
        {
            _source?.Close(window.Id);
        }
    }

    internal void ReceiveInput(string json)
    {
        BrowserInputEvent[] events = JsonSerializer.Deserialize(json, BrowserInputJsonContext.Default.BrowserInputEventArray)!;
        ulong? lastDomCapture = null;
        ulong capture = 0;
        foreach (BrowserInputEvent data in events)
        {
            if (data.Capture is null || data.Capture != lastDomCapture)
            {
                capture = _source?.BeginAcquisition() ?? 0;
                lastDomCapture = data.Capture;
            }

            switch (data.Type)
            {
                case 0:
                    Key key = PhysicalKeys.FromDomCode(data.Code ?? string.Empty);
                    if (data.Down)
                    {
                        _publishedTextKeys.Add(key);
                    }
                    else
                    {
                        _publishedTextKeys.Remove(key);
                    }

                    _source?.Keyboard(_window!.Id, new KeyData(key, data.Down, data.Repeat), capture);
                    break;
                case 1:
                    _source?.Mouse(_window!.Id, new MouseMoveData(new Vector2((float)data.X, (float)data.Y)), capture);
                    break;
                case 2:
                    MouseButton button = (int)data.X switch
                    {
                        0 => MouseButton.Left,
                        1 => MouseButton.Middle,
                        2 => MouseButton.Right,
                        3 => MouseButton.X1,
                        _ => MouseButton.X2,
                    };
                    _source?.Mouse(_window!.Id, new MouseButtonData(button, data.Down), capture);
                    break;
                case 3:
                    _source?.Mouse(_window!.Id, new MouseWheelData(new Vector2((float)data.X, (float)data.Y)), capture);
                    break;
                case 5:
                    MouseButton canceled = (int)data.X switch
                    {
                        0 => MouseButton.Left,
                        1 => MouseButton.Middle,
                        2 => MouseButton.Right,
                        3 => MouseButton.X1,
                        _ => MouseButton.X2,
                    };
                    _source?.Mouse(_window!.Id, new MouseButtonCanceledData(canceled), capture);
                    break;
                case 6:
                    if (_source is not null)
                    {
                        var error = new InvalidOperationException("Browser input acquisition exceeded 16384 events; stop the source explicitly.");
                        _source.FailAcquisition(error);
                        throw error;
                    }

                    break;
                case 4:
                    _source?.Focus(_window!.Id, data.Down);
                    break;
            }
        }
    }

    internal void Release() => _window = null;

    internal void Release(BrowserClipboard clipboard) => _clipboards.Remove(clipboard);

    internal void Notify<T>(Action<T>? handlers, T value) => _notifications.Enqueue(() =>
    {
        if (handlers is not null)
        {
            foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
            {
                Execute(() => handler(value));
            }
        }
    });

    private void Execute(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _errors.Add(ex);
        }
    }

    private sealed class TextInputRegistration(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
