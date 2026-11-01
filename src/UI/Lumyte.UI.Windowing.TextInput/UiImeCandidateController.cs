using Lumyte.Graphics.Drawing2D;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.TextEditing;
using Lumyte.UI.TextEditing;
using CandidateView = Lumyte.UI.TextEditing.Compose.Definitions.ImeCandidateView;

namespace Lumyte.UI.Windowing.TextInput;

/// <summary>Coordinates candidate presentation, generation-checked commands and complete resource retirement.</summary>
public sealed class UiImeCandidateController : IDisposable
{
    private readonly UiTextInputConnection _connection;
    private readonly UiImeCandidatePresenter _presenter;
    private readonly UiPopupManager _router;
    private readonly UiImeCandidateOptions _options;
    private readonly Action<ImeCandidateState>? _onStateChanged;
    private readonly Action<ImeCandidateResult>? _onCommandCompleted;
    private readonly Queue<PendingCommand> _commands = new();
    private readonly List<(PendingCommand Pending, Task<ImeCandidateResult> Task)> _submitted = [];
    private readonly TimeProvider _clock;
    private UiWindowId? _window;
    private UiSurface? _surface;
    private ImeCandidateViewElement? _view;
    private UiWindowBinding? _binding;
    private Task<UiWindowBinding>? _bindingTask;
    private IDisposable? _input;
    private TaskCompletionSource? _preparation;
    private CancellationToken _preparationToken;
    private ITextInputCandidateSession? _backend;
    private ImeCandidateSnapshot? _snapshot;
    private Task? _systemReturn;
    private Task? _retirement;
    private UiWindowRequestId? _close;
    private UiWindowRequestId? _place;
    private UiWindowRequestId? _show;
    private Rect2D? _lastAnchor;
    private ImeCandidateListId? _timedElement;
    private long _elementStart;
    private bool _retiring;
    private bool _stopping;
    private bool _disposed;
    private bool _failed;
    private bool _shown;
    private ImeCandidateState _state = new(null, ImeCandidatePresentationState.System, string.Empty);

    /// <summary>Initializes a new instance of the <see cref = "UiImeCandidateController"/> class.</summary>
    /// <param name = "connection">The connection.</param>
    /// <param name = "presenter">The presenter.</param>
    /// <param name = "router">The router.</param>
    /// <param name = "options">The options.</param>
    /// <param name = "onStateChanged">The on state changed.</param>
    /// <param name = "onCommandCompleted">The on command completed.</param>
    /// <param name = "clock">The clock.</param>
    public UiImeCandidateController(UiTextInputConnection connection, UiImeCandidatePresenter presenter, UiPopupManager router, UiImeCandidateOptions? options = null, Action<ImeCandidateState>? onStateChanged = null, Action<ImeCandidateResult>? onCommandCompleted = null, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(router);
        options ??= new();
        if (!float.IsFinite(options.ClientSize.X) || !float.IsFinite(options.ClientSize.Y) || options.ClientSize.X <= 0 || options.ClientSize.Y <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _connection = connection;
        _presenter = presenter;
        _router = router;
        _options = options with
        {
        };
        _onStateChanged = onStateChanged;
        _onCommandCompleted = onCommandCompleted;
        _clock = clock ?? TimeProvider.System;
        connection.AttachCandidateController(this);
    }

    /// <summary>Gets the state.</summary>
    public ImeCandidateState State => _state;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    public bool IsStopped => _stopping && _window is null && _bindingTask is null && _submitted.Count == 0 && (_systemReturn is null || _systemReturn.IsCompleted);

    internal bool IsPresentationIdle => !_shown && !_retiring && _snapshot is null;

    /// <summary>Capture Snapshot.</summary>
    /// <returns>The operation result.</returns>
    public ImeCandidateSnapshot? CaptureSnapshot() => _snapshot;

    /// <summary>Prepare Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_stopping)
        {
            throw new InvalidOperationException("The candidate controller is stopping.");
        }

        if (_options.Policy == ImeCandidateUiPolicy.System)
        {
            return ValueTask.CompletedTask;
        }

        if (_binding is not null && !_retiring)
        {
            return ValueTask.CompletedTask;
        }

        if (_preparation is { Task.IsCompleted: false })
        {
            return new(_preparation.Task);
        }

        _preparation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _preparationToken = cancellationToken;
        try
        {
            if (!_presenter.Windows.TryGetSnapshot(_connection.Window, out UiWindowSnapshot? owner) || owner is null)
            {
                throw new InvalidOperationException("The candidate owner is unavailable.");
            }

            var declaration = new CandidateView()
            {
                LayoutProvider = _presenter.Provider,
                Style = _presenter.Style,
                OnAction = Request,
            };
            _surface = new(declaration);
            _view = (ImeCandidateViewElement)_surface.RootElement;
            _surface.SetViewport(_options.ClientSize);
            var anchor = new UiWindowPlacement(owner.PresentationRoot, new(0, 0, 0, 0), UiWindowPlacementSide.Below);
            _window = _presenter.Windows.Create(new(_connection.Window, _options.ClientSize, UiWindowRole.ImeCandidates, UiWindowInputMode.Candidate, anchor, _options.WindowPolicy));
            _bindingTask = _presenter.Windows.BindPresentationAsync(_window.Value, _surface, cancellationToken).AsTask();
            _retiring = false;
            _lastAnchor = null;
            _close = null;
            _place = null;
            _show = null;
            _shown = false;
        }
        catch (Exception error)
        {
            _preparation.TrySetException(error);
            _retiring = true;
            _failed = true;
        }

        return new(_preparation.Task);
    }

    /// <summary>Request.</summary>
    /// <param name = "action">The action.</param>
    public void Request(ImeCandidateAction action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(action);
        if (_stopping || _retiring)
        {
            throw new InvalidOperationException("Candidate input is stopping.");
        }

        if (_commands.Count >= 4096)
        {
            throw new InvalidOperationException("The candidate command queue is full.");
        }

        _commands.Enqueue(new(action with { }, null, default));
    }

    /// <summary>Queues one candidate command and completes after its owner-thread result is observed.</summary>
    /// <param name = "action">The command with its expected list generation and revision.</param>
    /// <param name = "cancellationToken">Cancels waiting; an already queued command is not replayed.</param>
    /// <returns>The detached command result.</returns>
    public ValueTask<ImeCandidateResult> RequestAsync(ImeCandidateAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(action);
        if (_stopping || _retiring || _commands.Count >= 4096)
        {
            throw new InvalidOperationException("Candidate input is unavailable.");
        }

        var completion = new TaskCompletionSource<ImeCandidateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(new(action with { }, completion, cancellationToken));
        return new(completion.Task.WaitAsync(cancellationToken));
    }

    /// <summary>Advance.</summary>
    public void Advance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var failures = new List<Exception>();
        if (_bindingTask is { IsCompleted: true } preparing)
        {
            _bindingTask = null;
            try
            {
                _binding = preparing.GetAwaiter().GetResult();
                if (!_retiring && !_stopping)
                {
                    _input = _router.RegisterCandidateInput(_window!.Value, _surface!);
                    _preparation?.TrySetResult();
                }
            }
            catch (Exception error)
            {
                _failed = true;
                _retiring = true;
                SetState(new(null, ImeCandidatePresentationState.Failed, "candidate-presentation-failed"), failures);
                _preparation?.TrySetException(error);
            }
        }

        if (_preparation is { Task.IsCompleted: false } && _preparationToken.IsCancellationRequested)
        {
            _retiring = true;
        }

        ITextInputCandidateSession? current = _connection.CandidateSession;
        if (!ReferenceEquals(current, _backend))
        {
            bool previousBackend = _backend is not null;
            ReturnToSystem();
            _backend = current;
            if (previousBackend || _shown)
            {
                _retiring = true;
            }
        }

        if (!_retiring && _window is { } observed && _presenter.Windows.TryGetSnapshot(observed, out UiWindowSnapshot? presentation) && presentation!.State is UiWindowState.Closing or UiWindowState.Closed or UiWindowState.Failed)
        {
            ReturnToSystem();
            _retiring = true;
            _failed = true;
            if (_backend?.CaptureCandidates() is not null)
            {
                _connection.Reset();
            }

            SetState(new(_snapshot?.Id, ImeCandidatePresentationState.Failed, "candidate-presentation-lost"), failures);
        }

        if (_failed && _shown && _backend?.CaptureCandidates() is null)
        {
            _retiring = true;
        }

        if (_stopping)
        {
            ReturnToSystem();
            _retiring = true;
        }

        if (!_retiring && !_failed && _backend is { } backend)
        {
            bool usable = _options.Policy == ImeCandidateUiPolicy.PreferCustom && _binding is not null && _input is not null && backend.CandidateSupport.CanReadList && backend.CandidateSupport.CanSuppressSystemUi;
            backend.ConfigureCandidateUi(_options.Policy, usable);
            ImeCandidateSnapshot? snapshot = backend.CaptureCandidates();
            ImeCandidateState state = backend.CandidateState;
            if (state.Id is { } id && _timedElement != id)
            {
                _timedElement = id;
                _elementStart = _clock.GetTimestamp();
            }

            if (!_shown && state.State is ImeCandidatePresentationState.Preparing or ImeCandidatePresentationState.Custom && _timedElement is not null && _clock.GetElapsedTime(_elementStart) >= TimeSpan.FromSeconds(2))
            {
                ReturnToSystem();
                _retiring = true;
                SetState(new(state.Id, ImeCandidatePresentationState.ReturningToSystem, "candidate-show-timeout"), failures);
            }
            else if (snapshot is not null && usable)
            {
                snapshot = ImeCandidateValidation.Copy(snapshot);
                if (_snapshot?.Id != snapshot.Id || _snapshot.Revision != snapshot.Revision || _snapshot.DocumentRevision != snapshot.DocumentRevision)
                {
                    _snapshot = snapshot;
                    _view!.SetCandidates(snapshot);
                }

                if (_shown)
                {
                    SetState(state, failures);
                }
            }
            else if (_shown || _snapshot is not null)
            {
                _retiring = true;
                ReturnToSystem();
            }
            else
            {
                SetState(state, failures);
            }
        }

        while (_commands.TryDequeue(out PendingCommand? pending))
        {
            ImeCandidateAction action = pending.Action;
            if (pending.Cancellation.IsCancellationRequested)
            {
                Complete(pending, new(action, false, "canceled-before-submission"), failures);
                continue;
            }

            if (_stopping || _retiring || _backend is null)
            {
                Complete(pending, new(action, false, "candidate-unavailable"), failures);
            }
            else
            {
                try
                {
                    _submitted.Add((pending, _backend.RequestCandidateAsync(action, pending.Cancellation).AsTask()));
                }
                catch (Exception)
                {
                    Complete(pending, new(action, false, "candidate-command-failed"), failures);
                }
            }
        }

        foreach ((PendingCommand pending, Task<ImeCandidateResult> task) in _submitted.Where(p => p.Task.IsCompleted).ToArray())
        {
            _submitted.Remove((pending, task));
            ImeCandidateAction action = pending.Action;
            ImeCandidateResult result;
            try
            {
                result = task.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                result = new(action, false, "candidate-command-failed");
            }

            Complete(pending, result, failures);
        }

        if (_retiring)
        {
            Drain(failures);
        }
        else if (!_stopping && !_failed && _options.Policy == ImeCandidateUiPolicy.PreferCustom && _window is null)
        {
            _ = PrepareAsync();
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>Publish Geometry.</summary>
    public void PublishGeometry()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_retiring || _snapshot is null || _binding is null || _window is not { } window || _connection.Geometry is not { } geometry || geometry.DocumentRevision != _snapshot.DocumentRevision || !_presenter.Windows.TryGetSnapshot(_connection.Window, out UiWindowSnapshot? owner) || owner is null)
        {
            return;
        }

        var anchor = new Rect2D(geometry.Caret.X, geometry.Caret.Y, geometry.Caret.Width, geometry.Caret.Height);
        if (_lastAnchor != anchor)
        {
            _place = _presenter.Windows.Place(window, new(owner.PresentationRoot, anchor, UiWindowPlacementSide.Below));
            _lastAnchor = anchor;
        }

        if (_place is { } placement && _presenter.Windows.TryGetRequest(placement, out UiWindowRequestResult? placed) && placed!.Status == UiWindowRequestStatus.Succeeded)
        {
            _show ??= _presenter.Windows.Show(window);
        }

        if (_show is { } show && _presenter.Windows.TryGetRequest(show, out UiWindowRequestResult? shown) && shown!.Status == UiWindowRequestStatus.Succeeded)
        {
            _shown = true;
        }
    }

    /// <summary>Begin Stop.</summary>
    public void BeginStop()
    {
        if (!_disposed)
        {
            _stopping = true;
            _backend?.ConfigureCandidateUi(ImeCandidateUiPolicy.System, false);
            ReturnToSystem();
            _retiring = true;
        }
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!IsStopped)
        {
            throw new InvalidOperationException("BeginStop and advance until candidate resources have drained.");
        }

        _backend = null;
        _snapshot = null;
        _connection.DetachCandidateController(this);
        _disposed = true;
    }

    private void ReturnToSystem()
    {
        if (_backend is not null && _backend is not ITextInputSession { IsReleased: true } && _systemReturn is null)
        {
            _backend.ConfigureCandidateUi(ImeCandidateUiPolicy.System, false);
            _systemReturn = _backend.ReturnCandidatesToSystemAsync().AsTask();
        }
    }

    private void Drain(List<Exception> failures)
    {
        if (_systemReturn is { IsCompleted: false })
        {
            return;
        }

        if (_systemReturn is { } returned)
        {
            try
            {
                returned.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                _failed = true;
                SetState(new(_snapshot?.Id, ImeCandidatePresentationState.Failed, "candidate-system-restore-failed"), failures);
                if (!_stopping && _snapshot is not null && _binding is not null && _backend?.CaptureCandidates()?.Id == _snapshot.Id)
                {
                    // A failed System restore must not remove the only valid candidate UI.
                    _retiring = false;
                    return;
                }

                if (_backend?.CaptureCandidates() is not null)
                {
                    _connection.Reset();
                }
            }

            _systemReturn = null;
        }

        _input?.Dispose();
        _input = null;
        if (_bindingTask is { IsCompleted: false })
        {
            if (_window is { } preparing)
            {
                _close ??= _presenter.Windows.Close(preparing);
            }

            return;
        }

        if (_binding is not null)
        {
            _retirement ??= _binding.DisposeAsync().AsTask();
        }

        if (_retirement is { IsCompleted: false })
        {
            return;
        }

        if (_retirement is { } retired)
        {
            retired.GetAwaiter().GetResult();
            _retirement = null;
            _binding = null;
        }

        _surface?.Dispose();
        _surface = null;
        _view = null;
        if (_window is { } window)
        {
            _close ??= _presenter.Windows.Close(window);
            if (!_presenter.Windows.TryGetSnapshot(window, out UiWindowSnapshot? state) || state!.State is not (UiWindowState.Closed or UiWindowState.Failed))
            {
                return;
            }

            _presenter.Windows.Release(window);
            _window = null;
        }

        _snapshot = null;
        _shown = false;
        _retiring = false;
        _close = null;
        _place = null;
        _show = null;
        _lastAnchor = null;
        _timedElement = null;
        if (_preparationToken.IsCancellationRequested || _stopping)
        {
            _preparation?.TrySetCanceled(_preparationToken.IsCancellationRequested ? _preparationToken : new CancellationToken(true));
        }

        if (!_failed)
        {
            SetState(new(null, _stopping ? ImeCandidatePresentationState.Closed : ImeCandidatePresentationState.System, string.Empty), failures);
        }
    }

    private void SetState(ImeCandidateState state, List<Exception> failures)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;
        try
        {
            _onStateChanged?.Invoke(state);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    private void Complete(PendingCommand pending, ImeCandidateResult result, List<Exception> failures)
    {
        pending.Completion?.TrySetResult(result);
        try
        {
            _onCommandCompleted?.Invoke(result);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
    }

    private sealed record PendingCommand(ImeCandidateAction Action, TaskCompletionSource<ImeCandidateResult>? Completion, CancellationToken Cancellation);
}
