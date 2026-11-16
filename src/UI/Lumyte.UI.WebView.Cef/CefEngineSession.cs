using Xilium.CefGlue;

namespace Lumyte.UI.WebView.Cef;

internal sealed partial class CefEngineSession : IDisposable
{
    private readonly CefEngineRuntime _runtime;
    private readonly Action _wake;
    private readonly Client _client;
    private readonly Lock _gate = new();
    private readonly Queue<WebViewCommand> _commands = new();
    private readonly CefFocusAdmission _focusAdmission = new();
    private CefBrowser? _browser;
    private CefEngineSnapshot _snapshot;
    private CefEngineViewport _viewport = new(800, 600, 1, false, 0);
    private bool _dirty;
    private long _renderGeneration = -1;
    private bool _workPosted;
    private bool _closing;
    private bool _creationFailed;

    internal CefEngineSession(CefEngineRuntime runtime, Uri uri, WebViewPixelBudget budget, Action wake)
    {
        _runtime = runtime;
        _wake = wake;
        _client = new Client(this);
        _snapshot = new(UiWebViewState.Creating, uri, false, false, false, null, 0, 0, false, 0, 0, 0, 0, null, 0);
        ViewPixels = new(budget);
        PopupPixels = new(budget);
    }

    // Callback-owned full frames: top-down tightly-packed BGRA8, premultiplied alpha, sRGB.
    internal WebViewPixelMailbox ViewPixels { get; }

    internal WebViewPixelMailbox PopupPixels { get; }

    internal bool ImeAvailable => false;

    internal bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _snapshot.State == UiWebViewState.Closed;
            }
        }
    }

    private bool InputEnabled
    {
        get
        {
            lock (_gate)
            {
                return _viewport.Visible && _viewport.Width > 0 && _viewport.Height > 0 && !_closing;
            }
        }
    }

    public void Dispose()
    {
        _runtime.CheckOwner();
        if (!IsClosed)
        {
            throw new InvalidOperationException("cef-close-not-complete");
        }

        ViewPixels.Dispose();
        PopupPixels.Dispose();
        _runtime.ReleaseSession(this);
    }

    internal void Create()
    {
        try
        {
            var window = CefWindowInfo.Create();
            window.SetAsWindowless(IntPtr.Zero, true);
            var settings = new CefBrowserSettings
            {
                WindowlessFrameRate = 30,
                JavaScriptAccessClipboard = CefState.Disabled,
                JavaScriptDomPaste = CefState.Disabled,
                JavaScriptCloseWindows = CefState.Disabled,
            };
            CefBrowserHost.CreateBrowser(window, _client, settings, _snapshot.Uri!.AbsoluteUri, null, null);
        }
        catch
        {
            lock (_gate)
            {
                _creationFailed = true;
                _snapshot = _snapshot with { State = UiWebViewState.Faulted, FailureCode = "cef-create-failed" };
                Changed();
            }
        }
    }

    internal bool TryTakeSnapshot(out CefEngineSnapshot snapshot)
    {
        _runtime.CheckOwner();
        lock (_gate)
        {
            snapshot = _snapshot;
            bool result = _dirty;
            _dirty = false;
            _snapshot = _snapshot with { TakeFocusForward = null };
            return result;
        }
    }

    internal void Send(WebViewCommand command)
    {
        _runtime.CheckOwner();
        if (command.Uri is not null)
        {
            UiWebViewSession.ValidateUri(command.Uri);
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing || IsClosed, this);
            if (_snapshot.State == UiWebViewState.Faulted)
            {
                throw new InvalidOperationException(_snapshot.FailureCode);
            }

            if (_commands.Count >= 16)
            {
                throw new InvalidOperationException("cef-command-queue-full");
            }

            _commands.Enqueue(command);
            PostWork();
        }
    }

    internal void SetViewport(CefEngineViewport viewport)
    {
        _runtime.CheckOwner();
        if (viewport.Width < 0 || viewport.Height < 0 || !float.IsFinite(viewport.Scale) || viewport.Scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(viewport));
        }

        _ = checked((int)Math.Ceiling(viewport.Width * (double)viewport.Scale) * (int)Math.Ceiling(viewport.Height * (double)viewport.Scale) * 4);
        lock (_gate)
        {
            if (_closing || viewport == _viewport)
            {
                return;
            }

            if (viewport.Generation < _viewport.Generation)
            {
                throw new ArgumentOutOfRangeException(nameof(viewport));
            }

            _viewport = viewport;
            ViewPixels.Resize(viewport.Generation);
            PopupPixels.Resize(viewport.Generation);
            _snapshot = _snapshot with { Generation = viewport.Generation, PopupVisible = false };
            PostWork();
        }
    }

    internal void SetFocus(bool focused, long request)
    {
        _runtime.CheckOwner();
        lock (_gate)
        {
            if (_closing)
            {
                return;
            }

            _focusAdmission.Set(focused, request);
        }

        WithHost(host =>
        {
            ApplyPendingFocus(host.SetFocus, () =>
            {
                host.SendCaptureLostEvent();
                host.ImeCancelComposition();
            });
        });
    }

    internal void Pointer(CefEnginePointer input)
    {
        _runtime.CheckOwner();
        if (!InputEnabled && input.Kind != CefEnginePointerKind.Cancel)
        {
            return;
        }

        WithHost(host =>
        {
            var mouse = new CefMouseEvent { X = input.X, Y = input.Y, Modifiers = (CefEventFlags)input.Modifiers };
            switch (input.Kind)
            {
                case CefEnginePointerKind.Move: host.SendMouseMoveEvent(mouse, false); break;
                case CefEnginePointerKind.Leave: host.SendMouseMoveEvent(mouse, true); break;
                case CefEnginePointerKind.Down:
                case CefEnginePointerKind.Up: host.SendMouseClickEvent(mouse, (CefMouseButtonType)input.Button, input.Kind == CefEnginePointerKind.Up, input.ClickCount); break;
                case CefEnginePointerKind.Wheel: host.SendMouseWheelEvent(mouse, input.DeltaX, input.DeltaY); break;
                case CefEnginePointerKind.Cancel: host.SendCaptureLostEvent(); host.ImeCancelComposition(); break;
            }
        });
    }

    internal void Key(CefEngineKey input)
    {
        _runtime.CheckOwner();
        if (!InputEnabled)
        {
            return;
        }

        WithHost(host => host.SendKeyEvent(new CefKeyEvent { EventType = input.Up ? CefKeyEventType.KeyUp : CefKeyEventType.RawKeyDown, WindowsKeyCode = input.WindowsKeyCode, NativeKeyCode = input.NativeKeyCode, Modifiers = (CefEventFlags)input.Modifiers, IsSystemKey = input.IsSystemKey }));
    }

    internal void Text(string text)
    {
        _runtime.CheckOwner();
        if (!InputEnabled)
        {
            return;
        }

        if (text.Length > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(text));
        }

        WithHost(host =>
        {
            foreach (char character in text)
            {
                host.SendKeyEvent(new CefKeyEvent { EventType = CefKeyEventType.Char, Character = character, UnmodifiedCharacter = character, WindowsKeyCode = character });
            }
        });
    }

    internal void Close()
    {
        _runtime.CheckOwner();
        lock (_gate)
        {
            if (_closing || IsClosed)
            {
                return;
            }

            _closing = true;
            _commands.Clear();
            _snapshot = _snapshot with { State = _creationFailed ? UiWebViewState.Closed : UiWebViewState.Closing, Loading = false, PopupVisible = false };
            Changed();
            PostWork();
        }
    }

    private void WithHost(Action<CefBrowserHost> action)
    {
        lock (_gate)
        {
            if (_closing || _browser is null)
            {
                return;
            }

            // External pump uses the same owner thread; never synchronously marshal between threads.
            using CefBrowserHost host = _browser.GetHost();
            action(host);
        }
    }

    private void ApplyPendingFocus(Action<bool> setFocus, Action cancelCapture)
    {
        lock (_gate)
        {
            _focusAdmission.Apply(_closing, setFocus, cancelCapture);
        }
    }

    private void PostWork()
    {
        if (_workPosted)
        {
            return;
        }

        _workPosted = true;
        if (!CefRuntime.PostTask(CefThreadId.UI, new Work(this)))
        {
            _workPosted = false;
            _snapshot = _snapshot with { State = UiWebViewState.Faulted, FailureCode = "cef-post-failed" };
            Changed();
        }
    }

    private void RunWork()
    {
        lock (_gate)
        {
            _workPosted = false;
            if (_browser is null)
            {
                return;
            }

            using CefBrowserHost host = _browser.GetHost();
            ApplyPendingFocus(host.SetFocus, () =>
            {
                host.SendCaptureLostEvent();
                host.ImeCancelComposition();
            });
            if (_closing)
            {
                host.CloseBrowser(true);
                return;
            }

            _renderGeneration = _viewport.Generation;
            host.NotifyScreenInfoChanged();
            host.WasResized();
            host.WasHidden(!_viewport.Visible || _viewport.Width == 0 || _viewport.Height == 0);
            while (_commands.TryDequeue(out WebViewCommand command))
            {
                if (command.Sequence <= _snapshot.Sequence)
                {
                    continue;
                }

                _snapshot = _snapshot with { Sequence = command.Sequence };
                switch (command.Kind)
                {
                    case WebViewCommandKind.Navigate:
                        using (CefFrame frame = _browser.GetMainFrame())
                        {
                            frame.LoadUrl(command.Uri!.AbsoluteUri);
                        }

                        break;
                    case WebViewCommandKind.Back: _browser.GoBack(); break;
                    case WebViewCommandKind.Forward: _browser.GoForward(); break;
                    case WebViewCommandKind.Reload: _browser.Reload(); break;
                    case WebViewCommandKind.Stop: _browser.StopLoad(); break;
                }
            }
        }
    }

    private void Changed()
    {
        if (!_dirty)
        {
            _dirty = true;
            _wake();
        }
    }

    private sealed class Work(CefEngineSession owner) : CefTask
    {
        protected override void Execute() => owner.RunWork();
    }
}
