namespace Lumyte.UI.WebView;

/// <summary>A window-owned browser controller. Declaration rebuilds never issue commands.</summary>
/// <remarks>An engine dispatch exception faults the session, discards remaining creation commands and is rethrown. Accepted or ambiguous work is never automatically replayed.</remarks>
public sealed class UiWebViewSession
{
    private readonly Action _checkOwner;
    private readonly Action<WebViewCommand> _send;
    private readonly Queue<WebViewCommand> _pending = new();
    private object? _attachment;
    private long _sequence;
    private long _acceptedSequence;
    private long _notificationSequence;

    internal UiWebViewSession(Uri initialUri, Action checkOwner, Action<WebViewCommand> send)
    {
        ValidateUri(initialUri);
        InitialUri = initialUri;
        _checkOwner = checkOwner;
        _send = send;
    }

    /// <summary>Raised on the owner after a state snapshot changes.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised on the owner for untrusted JSON from an explicitly allowed main document.</summary>
    public event EventHandler<UiWebMessageReceivedEventArgs>? WebMessageReceived;

    internal event Action? PixelsChanged;

    internal event Action<bool>? FocusReturn;

    /// <summary>Gets a value indicating whether synchronous main-frame evaluation was negotiated.</summary>
    public bool ScriptAvailable { get; internal set; }

    /// <summary>Gets a value indicating whether origin-restricted page messages were negotiated and enabled.</summary>
    public bool HostMessagesAvailable { get; internal set; }

    /// <summary>Gets the browser lifetime.</summary>
    public UiWebViewState State { get; private set; }

    /// <summary>Gets the last main-document address observed on the owner.</summary>
    public Uri? CurrentUri { get; private set; }

    /// <summary>Gets a value indicating whether the current main document is loading.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Gets a value indicating whether history can move backward.</summary>
    public bool CanGoBack { get; private set; }

    /// <summary>Gets a value indicating whether history can move forward.</summary>
    public bool CanGoForward { get; private set; }

    /// <summary>Gets a non-sensitive browser failure code.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>Gets a value indicating whether a verified native IME path is available.</summary>
    public bool ImeAvailable => false;

    /// <summary>Gets a value indicating whether primary mouse, physical keys and committed text input were negotiated.</summary>
    public bool InputAvailable { get; internal set; }

    /// <summary>Gets a value indicating whether separate browser popup surfaces are connected.</summary>
    public bool PopupAvailable { get; internal set; }

    /// <summary>Gets a value indicating whether explicit post-creation navigation commands are connected.</summary>
    public bool NavigationCommandsAvailable { get; internal set; } = true;

    /// <summary>Gets the last unsupported capability request.</summary>
    public string? UnsupportedCapability { get; private set; }

    internal Func<string, TimeSpan, CancellationToken, Task<string>>? EvaluateScript { get; set; }

    internal Action? CancelHost { get; set; }

    internal IWebViewHost? Host { get; set; }

    internal Uri InitialUri { get; }

    internal Lumyte.Graphics.Drawing2D.Image2D? Image { get; set; }

    internal Lumyte.Graphics.Drawing2D.Image2D? PopupImage { get; set; }

    internal Lumyte.Graphics.Drawing2D.Rect2D PopupBounds { get; set; }

    internal bool PopupVisible { get; set; }

    /// <summary>Accepts an absolute HTTP(S) navigation, not a load-completion wait.</summary>
    /// <param name="uri">The destination.</param>
    /// <param name="cancellationToken">Cancels before command acceptance.</param>
    /// <returns>Command acceptance.</returns>
    public ValueTask NavigateAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ValidateUri(uri);
        return Command(WebViewCommandKind.Navigate, uri, cancellationToken);
    }

    /// <summary>Accepts a backward history command.</summary>
    /// <param name="cancellationToken">Cancels before acceptance.</param>
    /// <returns>Command acceptance.</returns>
    public ValueTask GoBackAsync(CancellationToken cancellationToken = default) => Command(WebViewCommandKind.Back, null, cancellationToken);

    /// <summary>Accepts a forward history command.</summary>
    /// <param name="cancellationToken">Cancels before acceptance.</param>
    /// <returns>Command acceptance.</returns>
    public ValueTask GoForwardAsync(CancellationToken cancellationToken = default) => Command(WebViewCommandKind.Forward, null, cancellationToken);

    /// <summary>Accepts an explicit reload command.</summary>
    /// <param name="cancellationToken">Cancels before acceptance.</param>
    /// <returns>Command acceptance.</returns>
    public ValueTask ReloadAsync(CancellationToken cancellationToken = default) => Command(WebViewCommandKind.Reload, null, cancellationToken);

    /// <summary>Accepts a stop-loading command.</summary>
    /// <param name="cancellationToken">Cancels before acceptance.</param>
    /// <returns>Command acceptance.</returns>
    public ValueTask StopLoadingAsync(CancellationToken cancellationToken = default) => Command(WebViewCommandKind.Stop, null, cancellationToken);

    /// <summary>Cancels the optional browser process without stopping the UI application.</summary>
    public void Cancel()
    {
        _checkOwner();
        CancelHost?.Invoke();
        Update(UiWebViewState.Faulted, failureCode: "browser-cancelled");
    }

    /// <summary>Evaluates a synchronous main-frame expression as JSON. Promises are not awaited; work is never replayed.</summary>
    /// <param name="script">An expression bounded to 3072 strict UTF-8 bytes.</param>
    /// <param name="timeout">A positive deadline up to 30 seconds, default five seconds.</param>
    /// <param name="cancellationToken">Cancels waiting, not already executing JavaScript.</param>
    /// <returns>The JSON result or a typed evaluation failure.</returns>
    public Task<string> EvaluateScriptAsync(string script, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        _checkOwner();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        if (new System.Text.UTF8Encoding(false, true).GetByteCount(script) > 3072)
        {
            throw new ArgumentOutOfRangeException(nameof(script));
        }

        TimeSpan deadline = timeout ?? TimeSpan.FromSeconds(5);
        if (deadline <= TimeSpan.Zero || deadline > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        ObjectDisposedException.ThrowIf(State is UiWebViewState.Closing or UiWebViewState.Closed, this);
        if (State != UiWebViewState.Ready)
        {
            throw new InvalidOperationException("cef-script-not-ready");
        }

        if (!ScriptAvailable || EvaluateScript is null)
        {
            throw new NotSupportedException("cef-script-unavailable");
        }

        return EvaluateScript(script, deadline, cancellationToken);
    }

    internal static void ValidateUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("An absolute HTTP(S) address is required.", nameof(uri));
        }
    }

    internal void ReceiveMessage(Uri source, string json)
    {
        _checkOwner();
        if (State == UiWebViewState.Ready && HostMessagesAvailable)
        {
            WebMessageReceived?.Invoke(this, new(source, json));
        }
    }

    internal void ReportUnsupportedInput()
    {
        UnsupportedCapability = "cef-input-unavailable";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ValidateAttachment(object attachment)
    {
        _checkOwner();
        if (_attachment is not null && !ReferenceEquals(_attachment, attachment))
        {
            throw new InvalidOperationException("The session is already attached.");
        }
    }

    internal void Attach(object attachment)
    {
        ValidateAttachment(attachment);
        _attachment = attachment;
    }

    internal void Detach(object attachment)
    {
        if (ReferenceEquals(_attachment, attachment))
        {
            Host?.Detach();
            _attachment = null;
        }
    }

    internal void Update(UiWebViewState state, Uri? uri = null, bool loading = false, bool back = false, bool forward = false, string? failureCode = null, long? sequence = null)
    {
        _checkOwner();

        // Late engine callbacks cannot resurrect or rewind a native lifetime.
        bool allowed = State switch
        {
            UiWebViewState.Creating => state is UiWebViewState.Creating or UiWebViewState.Ready or UiWebViewState.Faulted or UiWebViewState.Closing,
            UiWebViewState.Ready => state is UiWebViewState.Ready or UiWebViewState.Faulted or UiWebViewState.Closing,
            UiWebViewState.Faulted => state is UiWebViewState.Faulted or UiWebViewState.Closing,
            UiWebViewState.Closing => state is UiWebViewState.Closing or UiWebViewState.Closed,
            _ => false,
        };
        if (!allowed)
        {
            return;
        }

        bool stale = sequence is { } number && number < Math.Max(_acceptedSequence, _notificationSequence);
        if (stale && state == State && state is UiWebViewState.Creating or UiWebViewState.Ready)
        {
            return;
        }

        State = state;
        if (!stale)
        {
            CurrentUri = uri ?? CurrentUri;
            IsLoading = loading;
            CanGoBack = back;
            CanGoForward = forward;
            if (sequence is { } current)
            {
                _notificationSequence = current;
            }
        }

        if (state == UiWebViewState.Faulted)
        {
            FailureCode ??= failureCode ?? "browser-faulted";
        }

        if (state is UiWebViewState.Faulted or UiWebViewState.Closing or UiWebViewState.Closed)
        {
            IsLoading = false;
            _pending.Clear();
        }
        else if (state == UiWebViewState.Ready)
        {
            while (_pending.TryDequeue(out WebViewCommand command))
            {
                SendAccepted(command);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ReturnFocus(bool forward) => FocusReturn?.Invoke(forward);

    internal void NotifyPixelsChanged() => PixelsChanged?.Invoke();

    private ValueTask Command(WebViewCommandKind kind, Uri? uri, CancellationToken token)
    {
        _checkOwner();
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(State is UiWebViewState.Closing or UiWebViewState.Closed, this);
        if (State == UiWebViewState.Faulted)
        {
            throw new InvalidOperationException(FailureCode ?? "browser-faulted");
        }

        if (!NavigationCommandsAvailable)
        {
            throw new NotSupportedException("cef-navigation-commands-unavailable");
        }

        var command = new WebViewCommand(kind, uri, checked(++_sequence));
        if (State == UiWebViewState.Creating)
        {
            if (_pending.Count == 16)
            {
                throw new InvalidOperationException("The browser creation command queue is full.");
            }

            _pending.Enqueue(command);
        }
        else
        {
            SendAccepted(command);
        }

        return ValueTask.CompletedTask;
    }

    // A throwing engine send is ambiguous: fault once, drop remaining work, never replay it.
    private void SendAccepted(WebViewCommand command)
    {
        try
        {
            _send(command);
            _acceptedSequence = command.Sequence;
        }
        catch
        {
            State = UiWebViewState.Faulted;
            FailureCode = "command-dispatch-failed";
            IsLoading = false;
            _pending.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }
}
