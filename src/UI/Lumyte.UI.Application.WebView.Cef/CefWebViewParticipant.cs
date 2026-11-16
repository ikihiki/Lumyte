using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.UI.WebView;
using Lumyte.UI.WebView.Cef;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application.WebView.Cef;

internal sealed class CefWebViewParticipant(CefWebViewOptions options, IUiDispatcher dispatcher) : IUiApplicationParticipant
{
    private readonly Dictionary<Guid, WindowSessions> _windows = new();
    private readonly WebViewPixelBudget _cpu = new(options.CpuPixelBudgetBytes);
    private readonly WebViewPixelBudget _gpu = new(options.GpuImageBudgetBytes);
    private CefEngineRuntime? _runtime;
    private bool _stopping;
    private int _wakePending;

    public bool IsStopped => _stopping && _windows.Values.All(window => window.Retired);

    public TimeSpan? NextWorkDelay => _runtime?.NextWorkDelay;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _runtime = new([], options.ProfileDirectory, Wake);
        return ValueTask.CompletedTask;
    }

    public void OpenWindow(UiApplicationWindowContext context) => _windows.TryAdd(context.Id, new(context));

    public void CloseWindow(UiApplicationWindowContext context)
    {
        if (_windows.TryGetValue(context.Id, out WindowSessions? window))
        {
            if (window.Closing)
            {
                return;
            }

            window.Closing = true;
            foreach (Entry entry in window.Entries.Values)
            {
                entry.Session.Update(UiWebViewState.Closing);
                entry.Detach();
                entry.Engine.Close();
            }
        }
    }

    public bool IsWindowStopped(UiApplicationWindowContext context)
    {
        CloseWindow(context);
        return !_windows.TryGetValue(context.Id, out WindowSessions? window) || window.Entries.Values.All(entry => entry.Engine.IsClosed);
    }

    public void Advance()
    {
        Interlocked.Exchange(ref _wakePending, 0);
        _runtime?.Pump();
        foreach (WindowSessions window in _windows.Values.ToArray())
        {
            foreach (Entry entry in window.Entries.Values.ToArray())
            {
                if (window.Retired)
                {
                    continue;
                }

                entry.RefreshViewport(window.Closing);
                entry.AdvanceUpload(window.Closing);
                if (entry.Engine.TryTakeSnapshot(out CefEngineSnapshot state))
                {
                    entry.ReturnFocus(state);
                    entry.Session.PopupVisible = state.PopupVisible;
                    entry.Session.PopupBounds = new(state.PopupX, state.PopupY, state.PopupWidth, state.PopupHeight);
                    entry.Session.NotifyPixelsChanged();

                    // Native close is phase one; only phase two publishes Closed.
                    entry.Session.Update(state.State == UiWebViewState.Closed ? UiWebViewState.Closing : state.State, state.Uri, state.Loading, state.Back, state.Forward, state.FailureCode, state.Sequence);
                }
            }
        }
    }

    public void BeginStop()
    {
        _stopping = true;
        foreach (WindowSessions window in _windows.Values)
        {
            CloseWindow(window.Context);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!IsStopped)
        {
            throw new InvalidOperationException("cef-resources-not-retired");
        }

        _runtime?.Dispose();
        _runtime = null;
        return ValueTask.CompletedTask;
    }

    public async ValueTask RetireWindowResourcesAsync(UiApplicationWindowContext context)
    {
        if (_windows.TryGetValue(context.Id, out WindowSessions? window) && !window.Retired)
        {
            foreach (Entry entry in window.Entries.Values)
            {
                await entry.RetireAsync();
                entry.Session.Image = null;
                entry.Session.PopupImage = null;
                entry.Session.Host = null;
                entry.Engine.Dispose();
                entry.Session.Update(UiWebViewState.Closed);
            }

            window.Retired = true;
            _windows.Remove(context.Id);
        }
    }

    internal UiWebViewSession GetSession(UiApplicationWindowContext context, string key, Uri uri)
    {
        context.CheckOwner();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        UiWebViewSession.ValidateUri(uri);
        if (!_stopping)
        {
            _windows.TryAdd(context.Id, new(context));
        }

        if (_stopping || !_windows.TryGetValue(context.Id, out WindowSessions? window) || window.Closing)
        {
            throw new ObjectDisposedException(nameof(UiApplicationWindowContext));
        }

        if (window.Entries.TryGetValue(key, out Entry? existing))
        {
            if (existing.Session.InitialUri != uri)
            {
                throw new ArgumentException("The initial address for this session key cannot change.", nameof(uri));
            }

            return existing.Session;
        }

        if (_windows.Values.Sum(item => item.Entries.Count) >= options.MaximumSessions)
        {
            throw new InvalidOperationException("cef-session-budget-exceeded");
        }

        CefEngineSession engine = _runtime!.CreateSession(uri, _cpu, Wake);
        var session = new UiWebViewSession(uri, context.CheckOwner, engine.Send);
        var entry = new Entry(context, session, engine, _cpu, _gpu);
        session.Host = entry;
        window.Entries.Add(key, entry);
        return session;
    }

    private void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) != 0)
        {
            return;
        }

        // SynchronizationContext cleanup posts remain admitted during orderly shutdown.
        if (dispatcher is SynchronizationContext context)
        {
            context.Post(static _ => { }, null);
        }
    }

    private sealed class WindowSessions(UiApplicationWindowContext context)
    {
        internal UiApplicationWindowContext Context { get; } = context;

        internal Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);

        internal bool Closing { get; set; }

        internal bool Retired { get; set; }
    }

    private sealed class Entry(UiApplicationWindowContext window, UiWebViewSession session, CefEngineSession engine, WebViewPixelBudget cpu, WebViewPixelBudget gpu) : IWebViewHost
    {
        private readonly WebViewTextureUploader _uploader = new(cpu, gpu);
        private readonly WebViewTextureUploader _popupUploader = new(cpu, gpu);
        private Task<Image2D?>? _popupUpload;
        private Task<Image2D?>? _upload;
        private long _generation;
        private long _uploadGeneration;
        private long _popupUploadGeneration;

        private float _scale = 1;
        private bool _pointerDown;
        private long _focusRequest;
        private Vector2 _size;
        private bool _visible;

        internal UiWebViewSession Session { get; } = session;

        internal CefEngineSession Engine { get; } = engine;

        public void ValidateSurface(UiSurface surface)
        {
            if (!ReferenceEquals(UiApplicationViewSlot.ConstructingWindow, window) && !window.Windows.IsBound(window.Window, surface))
            {
                throw new InvalidOperationException("cef-session-window-mismatch");
            }
        }

        public void Viewport(Vector2 logicalSize, bool visible)
        {
            window.CheckOwner();
            if (!float.IsFinite(logicalSize.X) || !float.IsFinite(logicalSize.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(logicalSize));
            }

            float scale = window.Windows.TryGetSnapshot(window.Window, out UiWindowSnapshot? snapshot) && snapshot is not null ? snapshot.Scale.X : 1;
            if (!float.IsFinite(scale) || scale <= 0)
            {
                throw new InvalidOperationException("cef-invalid-window-scale");
            }

            if (_size != logicalSize || _visible != visible || _scale != scale)
            {
                _size = logicalSize;
                _visible = visible;
                _scale = scale;
                Engine.SetViewport(new(checked((int)Math.Ceiling(logicalSize.X)), checked((int)Math.Ceiling(logicalSize.Y)), scale, visible, checked(++_generation)));
            }
        }

        public void Focus(bool focused) => Engine.SetFocus(focused, checked(++_focusRequest));

        public void Detach()
        {
            Focus(false);
            Viewport(_size, false);
        }

        public void Input(UiInput input, Vector2? localPosition)
        {
            if (input is UiPointerInput pointer && localPosition is { } point)
            {
                CefEnginePointerKind kind = pointer.Phase switch
                {
                    UiPointerPhase.Down => CefEnginePointerKind.Down,
                    UiPointerPhase.Up => CefEnginePointerKind.Up,
                    UiPointerPhase.Cancel => CefEnginePointerKind.Cancel,
                    _ => CefEnginePointerKind.Move,
                };
                _pointerDown = pointer.Phase == UiPointerPhase.Down || (_pointerDown && pointer.Phase is not (UiPointerPhase.Up or UiPointerPhase.Cancel));
                Engine.Pointer(new(kind, checked((int)point.X), checked((int)point.Y), _pointerDown ? CefEngineModifiers.LeftButton : CefEngineModifiers.None));
            }
            else if (input is UiCommittedTextInput text)
            {
                Engine.Text(text.Text);
            }
            else if (input is UiKeyInput key)
            {
                int code = WindowsKey(key.Key);
                if (code != 0)
                {
                    CefEngineModifiers modifiers = (key.Modifiers.HasFlag(UiModifiers.Shift) ? CefEngineModifiers.Shift : 0)
                        | (key.Modifiers.HasFlag(UiModifiers.Control) ? CefEngineModifiers.Control : 0)
                        | (key.Modifiers.HasFlag(UiModifiers.Alt) ? CefEngineModifiers.Alt : 0)
                        | (key.IsRepeat ? CefEngineModifiers.Repeat : 0);
                    Engine.Key(new(!key.IsDown, code, 0, modifiers));
                }
            }
            else if (input is UiFocusLostInput or UiKeyCancelInput)
            {
                Focus(false);
            }
        }

        internal void RefreshViewport(bool closing)
        {
            if (!closing)
            {
                Viewport(_size, _visible);
            }
        }

        internal void ReturnFocus(CefEngineSnapshot state)
        {
            if (state.TakeFocusForward is { } forward && state.Generation == _generation && state.FocusRequest == _focusRequest)
            {
                Session.ReturnFocus(forward);
            }
        }

        internal void AdvanceUpload(bool closing)
        {
            if (_upload is { IsCompleted: true } completed)
            {
                _upload = null;
                Image2D? image = completed.GetAwaiter().GetResult();
                if (image is not null && !closing && _uploadGeneration == _generation)
                {
                    Session.Image = image;
                    Session.NotifyPixelsChanged();
                }
            }

            if (_popupUpload is { IsCompleted: true } completedPopup)
            {
                _popupUpload = null;
                Image2D? popup = completedPopup.GetAwaiter().GetResult();
                if (popup is not null && !closing && _popupUploadGeneration == _generation)
                {
                    Session.PopupImage = popup;
                    Session.NotifyPixelsChanged();
                }
            }

            if (!closing && _visible && Session.PopupVisible && _popupUpload is null)
            {
                _popupUploadGeneration = _generation;
                _popupUpload = window.Application.WithGraphicsAsync(device => _popupUploader.TryUploadAsync(device, Engine.PopupPixels)).AsTask();
            }

            if (!closing && _visible && _upload is null)
            {
                _uploadGeneration = _generation;
                _upload = window.Application.WithGraphicsAsync(device => _uploader.TryUploadAsync(device, Engine.ViewPixels)).AsTask();
            }
        }

        internal async ValueTask RetireAsync()
        {
            if (_upload is { } upload)
            {
                _upload = null;
                await upload;
            }

            if (_popupUpload is { } popupUpload)
            {
                _popupUpload = null;
                await popupUpload;
            }

            await _uploader.RetireAsync();
            await _popupUploader.RetireAsync();
        }

        private static int WindowsKey(Key key) => key switch
        {
            >= Key.A and <= Key.Z => 0x41 + (int)key - (int)Key.A,
            >= Key.Digit0 and <= Key.Digit9 => 0x30 + (int)key - (int)Key.Digit0,
            >= Key.F1 and <= Key.F24 => 0x70 + (int)key - (int)Key.F1,
            Key.Tab => 9,
            Key.Enter => 13,
            Key.Escape => 27,
            Key.Space => 32,
            Key.Backspace => 8,
            Key.Delete => 46,
            Key.Insert => 45,
            Key.Home => 36,
            Key.End => 35,
            Key.PageUp => 33,
            Key.PageDown => 34,
            Key.ArrowLeft => 37,
            Key.ArrowUp => 38,
            Key.ArrowRight => 39,
            Key.ArrowDown => 40,
            _ => 0,
        };
    }
}
