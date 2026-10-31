using Lumyte.TextEditing;
using Lumyte.UI.Automation;
using Lumyte.UI.Windowing.TextInput;

namespace Lumyte.UI.TextEditing.Automation;

/// <summary>Adds exclusive text and candidate automation to a borrowed surface host.</summary>
public sealed class UiTextAutomationHost : IUiAutomationHost, IUiAutomationTextHost, IUiAutomationImeHost, IUiAutomationRoutingHost, IUiAutomationScreenshotHost
{
    private readonly IUiAutomationHost _host;
    private readonly UiTextInputConnection _connection;
    private readonly UiImeCandidateController? _candidates;
    private readonly bool _allowInspection;

    /// <summary>Initializes a new instance of the <see cref = "UiTextAutomationHost"/> class.</summary>
    /// <param name = "host">The host.</param>
    /// <param name = "connection">The connection.</param>
    /// <param name = "candidates">The candidates.</param>
    /// <param name = "allowTextInspection">The allow text inspection.</param>
    public UiTextAutomationHost(IUiAutomationHost host, UiTextInputConnection connection, UiImeCandidateController? candidates = null, bool allowTextInspection = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(connection);
        _host = host;
        _connection = connection;
        _candidates = candidates;
        _allowInspection = allowTextInspection;
    }

    /// <summary>Gets the time provider.</summary>
    public TimeProvider TimeProvider => _host.TimeProvider;

    /// <summary>Probes pointer geometry using the borrowed host's routing scope.</summary>
    /// <param name="surface">The borrowed surface.</param>
    /// <param name="position">The logical pointer position.</param>
    /// <returns>The routed probe.</returns>
    public UiPointerProbe ProbePointer(UiSurface surface, System.Numerics.Vector2 position) => _host is IUiAutomationRoutingHost routing ? routing.ProbePointer(surface, position) : surface.ProbePointer(position);

    /// <summary>Can Receive Keys.</summary>
    /// <param name = "surface">The surface.</param>
    /// <returns>The operation result.</returns>
    public bool CanReceiveKeys(UiSurface surface) => _host is not IUiAutomationRoutingHost routing || routing.CanReceiveKeys(surface);

    /// <summary>Dispatch.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "input">The input.</param>
    /// <returns>The operation result.</returns>
    public bool Dispatch(UiSurface surface, UiInput input) => _host is IUiAutomationRoutingHost routing ? routing.Dispatch(surface, input) : surface.Dispatch(input);

    /// <summary>Capture Png Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<byte[]> CapturePngAsync(CancellationToken cancellationToken = default) => _host is IUiAutomationScreenshotHost screenshot ? screenshot.CapturePngAsync(cancellationToken) : throw new NotSupportedException("The host does not provide screenshots.");

    /// <summary>Run On Ui Thread Async.</summary>
    /// <typeparam name = "T">The t type.</typeparam>
    /// <param name = "action">The action.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => _host.RunOnUiThreadAsync(action, cancellationToken);

    /// <summary>Next Frame Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => _host.NextFrameAsync(cancellationToken);

    /// <summary>Acquire Input Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => _host.AcquireInputAsync(cancellationToken);

    /// <summary>Acquire Text Input Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<IUiAutomationInputLease> AcquireTextInputAsync(CancellationToken cancellationToken = default) => AcquireAsync(null, cancellationToken);

    /// <summary>Acquire Ime Candidate Input Async.</summary>
    /// <param name = "token">The token.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask<IUiAutomationInputLease> AcquireImeCandidateInputAsync(UiImeCandidateToken token, CancellationToken cancellationToken = default) => AcquireAsync(token, cancellationToken);

    /// <summary>Fill.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "editor">The editor.</param>
    /// <param name = "text">The text.</param>
    public void Fill(UiSurface surface, UiAutomationId editor, string text)
    {
        Compose.Definitions.TextBox target = Require(surface, editor);
        TextDocument document = target.Document!;
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        if (!ReferenceEquals(surface.FocusedWidget, target) || target.IsReadOnly || snapshot.Composition is not null)
        {
            throw new InvalidOperationException("The editor is read-only or composing.");
        }

        // One document transaction gives the same all-selection replacement as editing commands.
        if (!TextEditingCommands.ReplaceAll(document, text, TextEditOrigin.Automation))
        {
            throw new InvalidOperationException("The document changed before editing.");
        }
    }

    /// <summary>Read Value.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "editor">The editor.</param>
    /// <returns>The operation result.</returns>
    public string ReadValue(UiSurface surface, UiAutomationId editor)
    {
        if (!_allowInspection)
        {
            throw new NotSupportedException("Text inspection is disabled for this host.");
        }

        return Require(surface, editor).Document!.CaptureSnapshot().CommittedText;
    }

    /// <summary>Capture Ime Candidates.</summary>
    /// <param name = "surface">The surface.</param>
    /// <returns>The operation result.</returns>
    public UiImeCandidateSnapshot? CaptureImeCandidates(UiSurface surface)
    {
        if (!_allowInspection)
        {
            throw new NotSupportedException("Text inspection is disabled for this host.");
        }

        ImeCandidateSnapshot? snapshot = _candidates?.CaptureSnapshot();
        return snapshot is null ? null : new(Token(snapshot), Array.AsReadOnly(snapshot.Items.Select(i => i.Text).ToArray()), snapshot.SelectedIndex, snapshot.PageStarts, snapshot.CurrentPage, Array.AsReadOnly(Enum.GetValues<ImeCandidateActionKind>().Where(k => (snapshot.Operations & Operation(k)) != 0).Select(k => k.ToString().ToLowerInvariant()).ToArray()));
    }

    /// <summary>Execute Ime Candidate Async.</summary>
    /// <param name = "command">The command.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public async ValueTask<UiImeCandidateResult> ExecuteImeCandidateAsync(UiImeCandidateCommand command, CancellationToken cancellationToken = default)
    {
        ValueTask<ImeCandidateResult> pending = await _host.RunOnUiThreadAsync(
 _ =>
        {
            if (_candidates is null || _candidates.CaptureSnapshot() is not { } snapshot || Token(snapshot) != command.Token)
            {
                throw new InvalidOperationException("The candidate list changed.");
            }

            ImeCandidateActionKind kind = command.Kind switch
            {
                "select" => ImeCandidateActionKind.Select,
                "page" => ImeCandidateActionKind.Page,
                "finalize" => ImeCandidateActionKind.Finalize,
                "abort" => ImeCandidateActionKind.Abort,
                _ => throw new ArgumentException("Unknown candidate command.", nameof(command)),
            };
            return _candidates.RequestAsync(new(snapshot.Id, snapshot.Revision, kind, command.Index), cancellationToken);
        },
 cancellationToken);
        ImeCandidateResult result = await pending;
        return new(result.Accepted, result.Code);
    }

    private static UiImeCandidateToken Token(ImeCandidateSnapshot snapshot) => new(snapshot.Id.SessionId, snapshot.Id.SessionGeneration, snapshot.Id.ElementGeneration, snapshot.Revision);

    private static ImeCandidateOperations Operation(ImeCandidateActionKind kind) => kind switch
    {
        ImeCandidateActionKind.Select => ImeCandidateOperations.Select,
        ImeCandidateActionKind.Page => ImeCandidateOperations.Page,
        ImeCandidateActionKind.Finalize => ImeCandidateOperations.Finalize,
        _ => ImeCandidateOperations.Abort,
    };

    private Compose.Definitions.TextBox Require(UiSurface surface, UiAutomationId editor) => surface.FindWidget(editor) is Compose.Definitions.TextBox { Document: not null } target && ReferenceEquals(target.InputHandler, _connection) ? target : throw new InvalidOperationException("The editor attachment changed.");

    private async ValueTask<IUiAutomationInputLease> AcquireAsync(UiImeCandidateToken? token, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool idle = await _host.RunOnUiThreadAsync(_ => token is null ? _connection.IsInputIdle : _connection.IsCandidateInputIdle, cancellationToken);
            if (!idle)
            {
                await _host.NextFrameAsync(cancellationToken);
                continue;
            }

            IUiAutomationInputLease lease = await _host.AcquireInputAsync(cancellationToken);
            IDisposable? gate;
            try
            {
                gate = await _host.RunOnUiThreadAsync(
 _ =>
                {
                    if (!(token is null ? _connection.IsInputIdle : _connection.IsCandidateInputIdle))
                    {
                        return null;
                    }

                    if (token is not null && (_candidates?.CaptureSnapshot() is not { } current || Token(current) != token))
                    {
                        throw new InvalidOperationException("The candidate list changed before input acquisition.");
                    }

                    IDisposable guard = _connection.AcquireInputGate(token is not null);
                    if (lease is IUiAutomationOwnedRoutingLease owned)
                    {
                        try
                        {
                            owned.RegisterCleanup(guard);
                        }
                        catch
                        {
                            guard.Dispose();
                            throw;
                        }
                    }

                    return guard;
                },
 cancellationToken);
            }
            catch
            {
                await lease.DisposeAsync();
                throw;
            }

            if (gate is not null)
            {
                return lease is IUiAutomationOwnedRoutingLease ? lease : new Lease(_host, lease, gate);
            }

            await lease.DisposeAsync();
            await _host.NextFrameAsync(cancellationToken);
        }
    }

    private sealed class Lease(IUiAutomationHost host, IUiAutomationInputLease input, IDisposable gate) : IUiAutomationInputLease
    {
        private bool _disposed;

        /// <summary>Gets the pointer id.</summary>
        public long PointerId => input.PointerId;

        /// <summary>Dispatch.</summary>
        /// <param name = "surface">The surface.</param>
        /// <param name = "value">The value.</param>
        /// <returns>The operation result.</returns>
        public bool Dispatch(UiSurface surface, UiInput value) => input.Dispatch(surface, value);

        /// <summary>Dispose Async.</summary>
        /// <returns>The operation result.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await host.RunOnUiThreadAsync(
 _ =>
                {
                    gate.Dispose();
                    return true;
                },
 CancellationToken.None);
            }
            finally
            {
                await input.DisposeAsync();
            }
        }
    }
}
