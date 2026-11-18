using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.WebView;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application.WebView.Cef;

internal sealed class CefWebViewParticipant(CefWebViewOptions options, IUiDispatcher dispatcher, CefExecutionScope? execution = null) : IUiApplicationParticipant
{
    private readonly Dictionary<Guid, WindowSessions> _windows = new();
    private readonly WebViewPixelBudget _cpu = new(options.CpuPixelBudgetBytes);
    private readonly WebViewPixelBudget _gpu = new(options.GpuImageBudgetBytes);
    private readonly CefExecutionScope _execution = execution ?? new();
    private Task? _executionStop;
    private bool _stopping;
    private int _wakePending;

    public bool IsStopped
    {
        get
        {
            if (!_stopping)
            {
                return false;
            }

            if (_executionStop is { IsCompleted: true, IsCompletedSuccessfully: false } failed)
            {
                // Only completed tasks are read here: no synchronous owner wait.
                // Clearing the attempt lets RetryStopAsync retry the retained runtime,
                // even though UiApplication calls BeginStop only on its first attempt.
                _executionStop = null;
                failed.GetAwaiter().GetResult();
            }

            _executionStop ??= StopExecutionAsync();
            return _windows.Values.All(window => window.Retired) && _executionStop.IsCompletedSuccessfully;
        }
    }

    public TimeSpan? NextWorkDelay => null;

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_execution.StartAsync());
    }

    public Lumyte.UI.Compose.Definitions.Widget ProvideRootContext(UiApplicationWindowContext context, Lumyte.UI.Compose.Definitions.Widget root)
    {
        context.CheckOwner();
        if (!_windows.TryGetValue(context.Id, out WindowSessions? window))
        {
            window = new(context, this);
            _windows.Add(context.Id, window);
        }

        return UiWebViewHooks.ProvideWebViewSessions(window, root);
    }

    public void OpenWindow(UiApplicationWindowContext context) => _windows.TryAdd(context.Id, new(context, this));

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
        if (!_windows.TryGetValue(context.Id, out WindowSessions? window))
        {
            return true;
        }

        foreach (Entry entry in window.Entries.Values)
        {
            // A failed bounded stop attempt is distinct from native ownership
            // completion. Throw only at this retryable owner cleanup boundary.
            entry.Engine.ThrowIfStopFailed();
        }

        return window.Entries.Values.All(entry => entry.Engine.IsClosed);
    }

    public void Advance()
    {
        Interlocked.Exchange(ref _wakePending, 0);
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
                if (entry.Engine.TryTakeSnapshot(out HelperSnapshot state))
                {
                    entry.Session.InputAvailable = (entry.Engine.Features & 1) != 0;
                    entry.Session.ScriptAvailable = (entry.Engine.Features & 2) != 0;
                    entry.Session.HostMessagesAvailable = (entry.Engine.Features & 4) != 0 && entry.Engine.MessagesEnabled;
                    entry.Session.NotifyPixelsChanged();

                    // Native close is phase one; only phase two publishes Closed.
                    entry.Session.Update(state.State == UiWebViewState.Closed ? UiWebViewState.Closing : state.State, state.Uri, state.Loading, state.Back, state.Forward, state.FailureCode, state.Sequence);
                }

                while (entry.Engine.TryTakeMessage(out (long Epoch, Uri Source, string Json) message))
                {
                    entry.Session.ReceiveMessage(message.Source, message.Json);
                }

                while (entry.Engine.TryTakeFocusReturn(out bool forward))
                {
                    entry.Session.ReturnFocus(forward);
                }
            }
        }
    }

    public void BeginStop()
    {
        if (_stopping)
        {
            return;
        }

        _stopping = true;
        foreach (WindowSessions window in _windows.Values)
        {
            CloseWindow(window.Context);
        }

        _executionStop = StopExecutionAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (!IsStopped)
        {
            throw new InvalidOperationException("cef-resources-not-retired");
        }

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
                entry.Session.CancelHost = null;
                entry.Session.EvaluateScript = null;
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
            _windows.TryAdd(context.Id, new(context, this));
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

        CefHelperSession engine = new(uri, _cpu, Wake, options, _execution);
        var session = new UiWebViewSession(uri, context.CheckOwner, engine.Send);
        var entry = new Entry(context, session, engine, _cpu, _gpu);
        session.NavigationCommandsAvailable = false;
        session.EvaluateScript = engine.EvaluateAsync;
        session.Host = entry;
        session.CancelHost = engine.Close;
        window.Entries.Add(key, entry);
        return session;
    }

    private async Task StopExecutionAsync()
    {
        // BeginStop cancels each session before this wait. Runtime.StopAsync itself
        // waits for pending workflows before calling module.StopAsync.
        Task[] roots = _windows.Values.SelectMany(window => window.Entries.Values).Select(entry => entry.Engine.Completion).ToArray();
        try
        {
            await Task.WhenAll(roots);
            await _execution.StopAsync();
        }
        finally
        {
            // Failures need owner observation just as successful completions do.
            Wake();
        }
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

    private sealed class WindowSessions(UiApplicationWindowContext context, CefWebViewParticipant participant) : IUiWebViewSessions
    {
        internal UiApplicationWindowContext Context { get; } = context;

        internal Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);

        internal bool Closing { get; set; }

        internal bool Retired { get; set; }

        public UiWebViewSession GetSession(string key, Uri initialUri)
        {
            Context.CheckOwner();
            if (Closing || Retired)
            {
                throw new ObjectDisposedException(nameof(UiApplicationWindowContext));
            }

            return participant.GetSession(Context, key, initialUri);
        }
    }

    private sealed class Entry(UiApplicationWindowContext window, UiWebViewSession session, CefHelperSession engine, WebViewPixelBudget cpu, WebViewPixelBudget gpu) : IWebViewHost
    {
        private readonly WebViewTextureUploader _uploader = new(cpu, gpu);
        private readonly WebViewTextureUploader _popupUploader = new(cpu, gpu);
        private Task<Image2D?>? _popupUpload;
        private Task<Image2D?>? _upload;
        private long _generation;
        private long _uploadGeneration;
        private long _popupUploadGeneration;

        private float _scale = 1;
        private Vector2 _size;
        private Vector2 _wheelResidual;
        private bool _visible;
        private bool _uploadFaulted;

        internal UiWebViewSession Session { get; } = session;

        internal CefHelperSession Engine { get; } = engine;

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

            // Hiding/detaching can follow presentation failure after its native window vanished.
            // Only visible updates need a live snapshot; hidden updates retain the validated scale.
            float scale = _scale;
            if (visible)
            {
                scale = window.Windows.TryGetSnapshot(window.Window, out UiWindowSnapshot? snapshot) && snapshot is not null ? snapshot.Scale.X : 1;
            }

            if (!float.IsFinite(scale) || scale <= 0)
            {
                throw new InvalidOperationException("cef-invalid-window-scale");
            }

            if (_size != logicalSize || _visible != visible || _scale != scale)
            {
                _size = logicalSize;
                _visible = visible;
                _scale = scale;
                Engine.SetViewport(checked((int)Math.Ceiling(logicalSize.X)), checked((int)Math.Ceiling(logicalSize.Y)), scale, visible, checked(++_generation));
            }
        }

        public void Focus(bool focused)
        {
            window.CheckOwner();
            if (!focused)
            {
                _wheelResidual = default;
            }

            if (Session.State != UiWebViewState.Ready || !Session.InputAvailable)
            {
                return;
            }

            try
            {
                Engine.SendInput([CefInputCodec.Focus(focused, Engine.DocumentEpoch)]);
            }
            catch (InvalidOperationException)
            {
                Session.ReportUnsupportedInput();
            }
        }

        public void Detach()
        {
            Focus(false);
            Viewport(_size, false);
        }

        public void Input(UiInput input, Vector2? localPosition)
        {
            window.CheckOwner();
            if (input is UiFocusLostInput or UiKeyCancelInput or UiPointerInput { Phase: UiPointerPhase.Cancel })
            {
                _wheelResidual = default;
            }

            byte[][] bodies = [];
            bool encoded = Session.State == UiWebViewState.Ready && Session.InputAvailable && (input is UiWheelInput wheel
                ? CefInputCodec.TryEncodeWheel(wheel, localPosition, _scale, Engine.DocumentEpoch, ref _wheelResidual, out bodies)
                : CefInputCodec.TryEncode(input, localPosition, _scale, Engine.DocumentEpoch, out bodies));
            if (!encoded)
            {
                Session.ReportUnsupportedInput();
                return;
            }

            try
            {
                Engine.SendInput(bodies);
            }
            catch (InvalidOperationException)
            {
                Session.ReportUnsupportedInput();
            }
        }

        internal void RefreshViewport(bool closing)
        {
            if (!closing)
            {
                Viewport(_size, _visible);
            }
        }

        internal void AdvanceUpload(bool closing)
        {
            if (_upload is { IsCompleted: true } completed)
            {
                _upload = null;
                Image2D? image = CompleteUpload(completed);
                if (image is not null && !closing && !_uploadFaulted && _uploadGeneration == _generation)
                {
                    Session.Image = image;
                    Session.NotifyPixelsChanged();
                }
            }

            if (_popupUpload is { IsCompleted: true } completedPopup)
            {
                _popupUpload = null;
                Image2D? popup = CompleteUpload(completedPopup);
                if (popup is not null && !closing && !_uploadFaulted && _popupUploadGeneration == _generation)
                {
                    Session.PopupImage = popup;
                    Session.NotifyPixelsChanged();
                }
            }

            // A creating or unavailable optional browser has no upload work. In particular,
            // missing prerequisites must not ask the UI driver for graphics services.
            if (_uploadFaulted || Session.State != UiWebViewState.Ready)
            {
                return;
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
                try
                {
                    await upload;
                }
                catch (Exception)
                {
                    FailUpload();
                }
            }

            if (_popupUpload is { } popupUpload)
            {
                _popupUpload = null;
                try
                {
                    await popupUpload;
                }
                catch (Exception)
                {
                    FailUpload();
                }
            }

            await _uploader.RetireAsync();
            await _popupUploader.RetireAsync();
        }

        private Image2D? CompleteUpload(Task<Image2D?> completed)
        {
            try
            {
                return completed.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                FailUpload();
                return null;
            }
        }

        private void FailUpload()
        {
            if (_uploadFaulted)
            {
                return;
            }

            _uploadFaulted = true;
            Engine.Close();
            Session.Image = null;
            Session.PopupImage = null;
            Session.PopupVisible = false;
            Session.NotifyPixelsChanged();
            Session.Update(UiWebViewState.Faulted, failureCode: "cef-upload-failed");

            // Do not dispose uncertain GPU work here; normal retirement still owns its leases.
        }
    }
}
