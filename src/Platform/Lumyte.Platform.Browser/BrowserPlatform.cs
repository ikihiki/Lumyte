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
    private BufferedInputSource? _source;
    private BrowserWindow? _window;
    private ulong _nextWindow;
    private bool _pumping;
    private bool _notifying;
    private bool _disposed;

    private BrowserPlatform(JSObject canvas)
    {
        _canvas = canvas;
    }

    /// <inheritdoc/>
    public Guid Id { get; } = Guid.NewGuid();

    /// <inheritdoc/>
    public PlatformFeatures Features => PlatformFeatures.NativeWindows | PlatformFeatures.DeviceInput | (BrowserInterop.HasClipboard() ? PlatformFeatures.Clipboard : PlatformFeatures.None);

    /// <summary>Imports the JavaScript module and borrows an application-provided HTML canvas.</summary>
    /// <param name="canvas">An HTMLCanvasElement supporting WebGPU.</param>
    /// <param name="modulePath">The URL of the packaged JavaScript module.</param>
    /// <returns>A caller-owned platform. The canvas proxy remains owned by the caller.</returns>
    public static async Task<BrowserPlatform> CreateAsync(JSObject canvas, string modulePath = "./lumyte-windowing.js")
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        using JSObject module = await JSHost.ImportAsync("Lumyte.Platform.Browser", modulePath);
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
        if (_source is not null)
        {
            throw new InvalidOperationException("Only one input source is supported.");
        }

        _source = new BufferedInputSource(CheckMutation, _ => _source = null, "Browser");
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
    public ITextInputService CreateTextInputService() => throw new NotSupportedException("DOM text input is not included in this backend.");

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

    internal void ReceiveInput(string json)
    {
        BrowserInputEvent[] events = JsonSerializer.Deserialize(json, BrowserInputJsonContext.Default.BrowserInputEventArray)!;
        foreach (BrowserInputEvent data in events)
        {
            switch (data.Type)
            {
                case 0:
                    _source?.Keyboard(new KeyData(PhysicalKeys.FromDomCode(data.Code ?? string.Empty), data.Down, data.Repeat));
                    break;
                case 1:
                    _source?.Mouse(new MouseMoveData(new Vector2((float)data.X, (float)data.Y)));
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
                    _source?.Mouse(new MouseButtonData(button, data.Down));
                    break;
                case 3:
                    _source?.Mouse(new MouseWheelData(new Vector2((float)data.X, (float)data.Y)));
                    break;
                case 4:
                    _source?.Focus(data.Down);
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
}
