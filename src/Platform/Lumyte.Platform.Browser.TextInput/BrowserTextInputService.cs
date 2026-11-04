using System.Runtime.InteropServices.JavaScript;
using Lumyte.Input.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;

namespace Lumyte.Platform.Browser.TextInput;

/// <summary>Acquires Browser text through one textarea proxy and synchronizes editor focus at DOM boundaries.</summary>
public sealed class BrowserTextInputService : ITextInputService, ITextInputQuiescence, ITextInputInputGate
{
    private readonly BrowserPlatform _platform;
    private readonly JSObject _handle;
    private readonly JSObject _proxy;
    private readonly IDisposable _registration;
    private readonly Queue<TextInputKey> _keys = new();
    private BrowserTextSession? _session;
    private IDisposable? _focusBridge;
    private Exception? _bridgeFailure;
    private ulong _generation;
    private ulong _keySequence;
    private bool _disposed;
    private int _inputGates;

    private int _writeGates;

    private BrowserTextInputService(BrowserPlatform platform, JSObject handle)
    {
        _platform = platform;
        _handle = handle;
        _registration = platform.RegisterTextInput();
        try
        {
            _proxy = BrowserTextInterop.Proxy(handle);
        }
        catch
        {
            _registration.Dispose();
            throw;
        }
    }

    /// <summary>Gets the features.</summary>
    public TextInputFeatures Features => TextInputFeatures.CommittedText | TextInputFeatures.Composition | TextInputFeatures.Geometry;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsCandidateInputIdle => false;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsInputIdle => !_disposed && _keys.Count == 0 && (_session?.IsInputIdle ?? true);

    /// <summary>Create Async.</summary>
    /// <param name = "platform">The platform.</param>
    /// <param name = "modulePath">The module path.</param>
    /// <returns>The operation result.</returns>
    public static async ValueTask<BrowserTextInputService> CreateAsync(BrowserPlatform platform, string modulePath = "./lumyte-text-input.js")
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        platform.Check();
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location") ?? throw new NotSupportedException("Browser location is unavailable.");
        string href = location.GetPropertyAsString("href") ?? throw new NotSupportedException("Browser location is unavailable.");
        var uri = new Uri(new Uri(href), modulePath);
        await JSHost.ImportAsync("Lumyte.Platform.Browser.TextInput", uri.AbsoluteUri);
        JSObject handle = BrowserTextInterop.Create(platform.TextInputCanvas);
        try
        {
            return new(platform, handle);
        }
        catch
        {
            BrowserTextInterop.Destroy(handle);
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Begin.</summary>
    /// <param name = "target">The target.</param>
    /// <param name = "document">The document.</param>
    /// <param name = "navigator">The navigator.</param>
    /// <returns>The operation result.</returns>
    public ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator)
    {
        Check();
        if (_session is not null)
        {
            throw new InvalidOperationException("End and drain the current textarea session first.");
        }

        BrowserWindow window = _platform.TextInputWindow ?? throw new NotSupportedException("The canvas Window is unavailable.");
        if (target.FocusWindow != window.Id || target.PresentationWindow != window.Id)
        {
            throw new NotSupportedException("The textarea cannot address an unrelated Window.");
        }

        _platform.ResolveTextInputOrigin(window.Id);
        _platform.BeginTextInputScope();
        IDisposable proxyRegistration = _platform.RegisterTextProxy(_proxy, target.EditorId.ToString("N"));
        try
        {
            _session = new(_handle, target, document, navigator, checked(++_generation), proxyRegistration, QueueKey);
            _keySequence = 0;
            return _session;
        }
        catch
        {
            proxyRegistration.Dispose();
            throw;
        }
    }

    /// <summary>Register Focus Bridge.</summary>
    /// <param name = "dispatchInput">The dispatch input.</param>
    /// <param name = "synchronizeFocus">The synchronize focus.</param>
    /// <param name = "canDispatch">The can dispatch.</param>
    /// <returns>The operation result.</returns>
    public IDisposable RegisterFocusBridge(Action dispatchInput, Action synchronizeFocus, Func<bool> canDispatch)
    {
        Check();
        ArgumentNullException.ThrowIfNull(dispatchInput);
        ArgumentNullException.ThrowIfNull(synchronizeFocus);
        ArgumentNullException.ThrowIfNull(canDispatch);
        if (_focusBridge is not null)
        {
            throw new InvalidOperationException("The Browser focus bridge is already registered.");
        }

        _focusBridge = _platform.RegisterTextFocusBridge(() =>
        {
            if (!canDispatch())
            {
                return false;
            }

            try
            {
                _platform.PumpEvents();
                Advance();
                dispatchInput();
                synchronizeFocus();

                // Uncomposed sessions release and reconnect before the DOM event returns.
                for (int index = 0; index < 2; index++)
                {
                    Advance();
                    synchronizeFocus();
                }

                _session?.SynchronizeFromDocument();
                return true;
            }
            catch (Exception error)
            {
                _bridgeFailure = error;
                return false;
            }
        });
        return new FocusRegistration(this);
    }

    /// <summary>Advance.</summary>
    public void Advance()
    {
        Check();
        _session?.Advance();
        if (_session?.IsReleased == true)
        {
            _session = null;
        }

        _session?.SynchronizeFromDocument();
        if (_bridgeFailure is { } failure)
        {
            _bridgeFailure = null;
            throw new InvalidOperationException("Browser focus input could not be delivered synchronously.", failure);
        }
    }

    /// <summary>Drain Keys.</summary>
    /// <returns>The operation result.</returns>
    public IReadOnlyList<TextInputKey> DrainKeys()
    {
        Check();
        TextInputKey[] keys = _keys.ToArray();
        _keys.Clear();
        return Array.AsReadOnly(keys);
    }

    /// <summary>Acquire Input Gate.</summary>
    /// <param name = "allowComposition">The allow composition.</param>
    /// <returns>The operation result.</returns>
    public IDisposable AcquireInputGate(bool allowComposition = false)
    {
        Check();
        if (!allowComposition && _session?.Status.IsComposing == true)
        {
            throw new InvalidOperationException("An active composition must become idle before acquiring text input.");
        }

        _inputGates++;
        if (!allowComposition)
        {
            _writeGates++;
        }

        BrowserTextInterop.SetInputGate(_handle, _writeGates != 0);
        return new InputGate(this, allowComposition);
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Check();
        if (_session is not null)
        {
            throw new InvalidOperationException("End and drain the textarea before disposing its service.");
        }

        _focusBridge?.Dispose();
        _focusBridge = null;
        BrowserTextInterop.Destroy(_handle);
        _proxy.Dispose();
        _handle.Dispose();
        _registration.Dispose();
        _keys.Clear();
        _disposed = true;
    }

    private void QueueKey(BrowserTextSession session, BrowserTextPacket packet)
    {
        if (_keys.Count >= 4096)
        {
            session.RequestEnd(TextCompositionEnd.Cancel);
            throw new InvalidOperationException("The text key queue is full.");
        }

        InputWindowId origin = _platform.ResolveTextInputOrigin(session.Target.FocusWindow);
        InputCaptureStamp capture = _platform.ReserveTextInputCapture();
        Key key = Lumyte.Platform.Internal.PhysicalKeys.FromDomCode(packet.Code ?? string.Empty);
        _keys.Enqueue(new(origin, origin, capture, session.Status.SessionId, session.Generation, checked(++_keySequence), key, packet.Down, packet.Repeat, packet.Shift, packet.Control, packet.Alt, packet.Meta));
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _platform.Check();
    }

    private sealed class FocusRegistration(BrowserTextInputService service) : IDisposable
    {
        private BrowserTextInputService? _service = service;

        /// <summary>Dispose.</summary>
        public void Dispose()
        {
            BrowserTextInputService? current = Interlocked.Exchange(ref _service, null);
            current?._focusBridge?.Dispose();
            if (current is not null)
            {
                current._focusBridge = null;
            }
        }
    }

    private sealed class InputGate(BrowserTextInputService owner, bool allowComposition) : IDisposable
    {
        private BrowserTextInputService? _owner = owner;

        /// <summary>Dispose.</summary>
        public void Dispose()
        {
            BrowserTextInputService? current = _owner;
            if (current is null)
            {
                return;
            }

            current.Check();
            _owner = null;
            current._inputGates--;
            if (!allowComposition)
            {
                current._writeGates--;
            }

            BrowserTextInterop.SetInputGate(current._handle, current._writeGates != 0);
        }
    }
}
