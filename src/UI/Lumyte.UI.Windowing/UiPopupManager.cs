using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing;

/// <summary>Owns popup content, menu input scopes and cleanup above an injected window manager.</summary>
public sealed partial class UiPopupManager : IDisposable
{
    private readonly IUiWindowManager _windows;

    private readonly Dictionary<Guid, List<Func<CancellationToken, ValueTask<IAsyncDisposable>>>> _menuGuards = [];

    private readonly Dictionary<UiWindowId, UiSurface> _candidateSurfaces = [];

    private readonly Dictionary<Guid, Owner> _owners = [];

    private readonly Dictionary<Guid, Popup> _popups = [];

    private readonly Dictionary<long, UiWindowId?> _pointers = [];

    private readonly Dictionary<(InputDeviceId? Device, Key Key), UiWindowId?> _keys = [];

    private readonly Queue<Action> _commands = new();

    private readonly Queue<Action> _callbacks = new();

    private bool _busy;

    private bool _stopping;

    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UiPopupManager"/> class.</summary>
    /// <param name="windows">The borrowed manager, advanced by the host before this manager.</param>
    /// <param name="clock">Reserved host clock; window preparation uses the injected manager's clock.</param>
    public UiPopupManager(IUiWindowManager windows, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows;
    }

    /// <summary>Gets a value indicating whether all owned popup content and owner registrations have drained.</summary>
    public bool IsStopped => _stopping && _popups.Count == 0 && _owners.Count == 0;

    /// <summary>Registers an already bound normal window and surface as borrowed input destinations.</summary>
    /// <param name="window">The same manager's bound window.</param>
    /// <param name="surface">The borrowed surface.</param>
    /// <returns>An owner whose release must precede window teardown.</returns>
    public UiPopupOwner RegisterOwner(UiWindowId window, UiSurface surface)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(surface);
        if (!_windows.IsBound(window, surface) || _owners.Values.Any(o => o.Window == window || ReferenceEquals(o.Surface, surface)))
        {
            throw new ArgumentException("Owner must be uniquely bound to this window manager.");
        }

        UiPopupOwner? token = null;
        token = new(() => _commands.Enqueue(() => ReleaseOwner(token!.Id)));
        _owners.Add(token.Id, new(token, window, surface));
        return token;
    }

    /// <summary>Registers a barrier whose token is retained for each menu's complete lifetime.</summary>
    /// <param name="owner">The registered logical owner.</param>
    /// <param name="prepare">Begins cancellation-aware preparation at an owner update point.</param>
    /// <returns>A registration; disposing it affects future openings only.</returns>
    public IDisposable RegisterMenuGuard(UiPopupOwner owner, Func<CancellationToken, ValueTask<IAsyncDisposable>> prepare)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(prepare);
        if (!_owners.TryGetValue(owner.Id, out Owner? current) || !ReferenceEquals(current.Token, owner) || current.Releasing || current.Paused)
        {
            throw new ArgumentException("Unknown owner.", nameof(owner));
        }

        if (!_menuGuards.TryGetValue(owner.Id, out List<Func<CancellationToken, ValueTask<IAsyncDisposable>>>? guards))
        {
            guards = [];
            _menuGuards.Add(owner.Id, guards);
        }

        guards.Add(prepare);
        return new InputRegistration(() => _commands.Enqueue(() => guards.Remove(prepare)));
    }

    /// <summary>Registers a prepared candidate surface ahead of menu outside-press routing.</summary>
    /// <param name="window">A bound candidate-only window.</param>
    /// <param name="surface">The borrowed matching surface.</param>
    /// <returns>An owner-idle registration that cancels retained pointer series on disposal.</returns>
    public IDisposable RegisterCandidateInput(UiWindowId window, UiSurface surface)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(surface);
        if (!_windows.IsBound(window, surface) || !_windows.TryGetSnapshot(window, out UiWindowSnapshot? state) || state!.Role != UiWindowRole.ImeCandidates || state.InputMode != UiWindowInputMode.Candidate || !_candidateSurfaces.TryAdd(window, surface))
        {
            throw new ArgumentException("Candidate registration requires a unique bound candidate window.", nameof(window));
        }

        return new InputRegistration(() =>
        {
            _candidateSurfaces.Remove(window);
            CancelWindow(window, surface);
        });
    }

    /// <summary>Queues root popup construction; existing menu cleanup precedes replacement.</summary>
    /// <param name="owner">A live owner registered here.</param>
    /// <param name="request">Fixed content and callbacks.</param>
    /// <returns>A pending popup handle.</returns>
    public UiPopupHandle RequestOpen(UiPopupOwner owner, UiPopupRequest request)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(owner);
        if (!_owners.TryGetValue(owner.Id, out Owner? current) || !ReferenceEquals(current.Token, owner) || current.Releasing || owner.IsReleaseRequested)
        {
            throw new ArgumentException("Unknown or releasing owner.", nameof(owner));
        }

        return Enqueue(current, null, request);
    }

    /// <summary>Queues a submenu anchored to an open menu's surface.</summary>
    /// <param name="parent">An open non-passive parent.</param>
    /// <param name="request">Fixed submenu content and callbacks.</param>
    /// <returns>A pending child handle.</returns>
    public UiPopupHandle RequestOpen(UiPopupHandle parent, UiPopupRequest request)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(parent);
        if (!_popups.TryGetValue(parent.Id, out Popup? popup) || !ReferenceEquals(popup.Handle, parent) || popup.Mode == UiPopupInputMode.Passive || parent.State != UiPopupState.Open || parent.IsCloseRequested)
        {
            throw new ArgumentException("Parent must be an open menu.", nameof(parent));
        }

        return Enqueue(popup.Owner, popup, request);
    }

    /// <summary>Routes one acquisition-origin input exactly once, preserving series destinations.</summary>
    /// <param name="input">Origin and logical native-client input, already deduplicated by the acquisition router.</param>
    /// <returns>Whether UI or a menu scope consumed this input.</returns>
    public bool Dispatch(UiWindowInputEnvelope input)
    {
        Check();
        Enter();
        try
        {
            return Route(input);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Queues chain closure for a host-observed press outside the presentation root.</summary>
    /// <param name="owner">The affected registered root.</param>
    public void NotifyOutsidePress(UiPopupOwner owner)
    {
        Check();
        ArgumentNullException.ThrowIfNull(owner);
        if (!_owners.TryGetValue(owner.Id, out Owner? current) || !ReferenceEquals(owner, current.Token))
        {
            throw new ArgumentException("Unknown owner.", nameof(owner));
        }

        _commands.Enqueue(() =>
        {
            foreach (Popup p in _popups.Values.Where(p => p.Owner == current && p.Mode == UiPopupInputMode.Menu).ToArray())
            {
                RequestDismiss(p, UiPopupCloseReason.OutsidePress);
            }
        });
    }

    /// <summary>Advances queued requests and asynchronous cleanup once after window-manager Advance.</summary>
    public void Advance()
    {
        Check();
        Enter();
        var failures = new List<Exception>();
        try
        {
            int commands = _commands.Count;
            for (int i = 0; i < commands; i++)
            {
                try
                {
                    _commands.Dequeue()();
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }

            foreach (Popup popup in _popups.Values.OrderByDescending(p => p.Depth).ToArray())
            {
                try
                {
                    Step(popup);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                    if (popup.Handle.State is not (UiPopupState.Closed or UiPopupState.Failed))
                    {
                        popup.Handle.FailureCode = "presentation-failed";
                        Close(popup, UiPopupCloseReason.OpenFailed);
                    }
                }
            }

            foreach (Owner owner in _owners.Values.Where(o => o.Releasing && !_popups.Values.Any(p => p.Owner == o)).ToArray())
            {
                try
                {
                    CancelWindow(owner.Window, owner.Surface);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }

                owner.Token.IsReleased = true;
                _owners.Remove(owner.Token.Id);
                _menuGuards.Remove(owner.Token.Id);
            }

            int callbacks = _callbacks.Count;
            for (int i = 0; i < callbacks; i++)
            {
                try
                {
                    _callbacks.Dequeue()();
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
            }
        }
        finally
        {
            _busy = false;
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>Copies current popup identities and state without retaining UI references.</summary>
    /// <returns>Detached popup records.</returns>
    public IReadOnlyList<UiPopupInfo> CapturePopups()
    {
        Check();
        return Array.AsReadOnly(_popups.Values.Select(p => new UiPopupInfo(p.Handle.Id, p.Owner.Token.Id, p.Parent?.Handle.Id, p.SurfaceId, p.Handle.State, p.Handle.Presentation, p.Mode, p.Handle.FallbackCode)).ToArray());
    }

    /// <summary>Borrows a currently open popup surface for owner-thread adapters.</summary>
    /// <param name="popupId">The explicit popup identity.</param>
    /// <param name="surface">The borrowed surface; bind adapter cleanup with BindLifetime.</param>
    /// <returns>Whether the popup remains open.</returns>
    public bool TryGetSurface(Guid popupId, out UiSurface? surface)
    {
        Check();
        surface = _popups.TryGetValue(popupId, out Popup? p) && p.Handle.State == UiPopupState.Open ? p.Surface : null;
        return surface is not null;
    }

    /// <summary>Transfers asynchronous adapter cleanup into this popup's close drain.</summary>
    /// <param name="popupId">An open popup identity.</param>
    /// <param name="participant">A unique participant whose disposal cancels and drains its jobs.</param>
    public void BindLifetime(Guid popupId, IAsyncDisposable participant)
    {
        Check();
        ArgumentNullException.ThrowIfNull(participant);
        if (!_popups.TryGetValue(popupId, out Popup? p) || p.Handle.State != UiPopupState.Open || _popups.Values.Any(current => current.Participants.Contains(participant, ReferenceEqualityComparer.Instance)))
        {
            throw new InvalidOperationException("Only an open popup accepts unique cleanup participants.");
        }

        p.Participants.Add(participant);
    }

    /// <summary>Stops admission and queues all borrowed-owner and popup cleanup.</summary>
    public void BeginStop()
    {
        Check();
        _stopping = true;
        _commands.Enqueue(() =>
        {
            foreach (Owner owner in _owners.Values.ToArray())
            {
                ReleaseOwner(owner.Token.Id);
            }
        });
    }

    /// <summary>Disposes after BeginStop and repeated Advance have completed the entire drain.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BeginStop();
        if (!IsStopped)
        {
            throw new InvalidOperationException("Continue frame processing until IsStopped.");
        }

        _commands.Clear();
        _disposed = true;
    }

    /// <summary>Probes the same current window, menu scope and component routing as ordinary input.</summary>
    /// <param name="surface">The registered surface.</param>
    /// <param name="position">The surface-local pointer position.</param>
    /// <returns>The current hit and accepting activation target without sending input.</returns>
    public UiPointerProbe ProbePointer(UiSurface surface, Vector2 position)
    {
        UiWindowId? window = FindWindow(surface);
        if (window is null || !_windows.TryGetSnapshot(window.Value, out UiWindowSnapshot? snapshot) || snapshot!.State != UiWindowState.Open || !_windows.TryTransformPoint(window.Value, snapshot.PresentationRoot, position, out Vector2 rootPoint))
        {
            return default;
        }

        UiWindowHit? hit = _windows.HitTest(snapshot.PresentationRoot, rootPoint);
        Popup? menu = Deepest(snapshot.KeyboardScopeId);
        return hit?.Window == window && (menu is null || _popups.Values.Any(p => p.Window == window && p.Handle.State == UiPopupState.Open)) ? surface.ProbePointer(position) : default;
    }

    internal UiWindowId? FindWindow(UiSurface surface) => _owners.Values.FirstOrDefault(o => ReferenceEquals(o.Surface, surface))?.Window ?? _popups.Values.FirstOrDefault(p => ReferenceEquals(p.Surface, surface))?.Window;

    internal bool CanAnimate(Guid popupId)
    {
        return _popups.TryGetValue(popupId, out Popup? popup) && popup.Handle.State == UiPopupState.Open && !popup.Closing && !popup.Owner.Paused && !popup.Owner.Releasing
            && popup.Window is { } window && _windows.TryGetSnapshot(window, out UiWindowSnapshot? state)
            && state is { IsVisible: true, ClientSize.X: > 0, ClientSize.Y: > 0 }
            && _windows.TryGetSnapshot(popup.Owner.Window, out UiWindowSnapshot? owner) && owner is { IsVisible: true };
    }

    internal bool CanReceiveKeys(UiSurface surface)
    {
        UiWindowId? window = FindWindow(surface);
        if (window is null || !ReferenceEquals(Surface(window.Value), surface) || !_windows.TryGetSnapshot(window.Value, out UiWindowSnapshot? snapshot) || !snapshot!.IsVisible)
        {
            return false;
        }

        Popup? menu = Deepest(snapshot!.KeyboardScopeId);
        return menu is not null ? ReferenceEquals(menu.Surface, surface) && menu.Handle.State == UiPopupState.Open : _windows.GetKeyboardTarget(window.Value) == window;
    }

    internal bool Dispatch(UiSurface surface, UiInput input)
    {
        UiWindowId window = FindWindow(surface) ?? throw new InvalidOperationException("popup-unavailable");
        if (!_windows.TryGetSnapshot(window, out UiWindowSnapshot? snapshot) || snapshot!.InputOrigin is not { } origin)
        {
            throw new InvalidOperationException("origin-input-unavailable");
        }

        if (input is UiPointerInput pointer)
        {
            if (!_windows.TryTransformPoint(window, snapshot.PresentationRoot, pointer.Position, out Vector2 at))
            {
                throw new InvalidOperationException("coordinate-unavailable");
            }

            input = pointer with
            {
                Position = at,
            };
        }

        return Dispatch(new UiWindowInputEnvelope(origin, input));
    }

    internal void UpdateDeclaration(UiPopupHandle handle, Widget content, UiWindowPlacementSide side, bool alignEnd)
    {
        if (_popups.TryGetValue(handle.Id, out Popup? popup) && !popup.Closing && popup.Request is { } request)
        {
            request.Side = side;
            request.AlignEnd = alignEnd;
            popup.Surface?.Rebuild(content);
        }
    }

    private static Task DisposeParticipant(IAsyncDisposable participant)
    {
        try
        {
            return participant.DisposeAsync().AsTask();
        }
        catch (Exception error)
        {
            return Task.FromException(error);
        }
    }

    private static Task<IAsyncDisposable> PrepareGuard(Func<CancellationToken, ValueTask<IAsyncDisposable>> prepare, CancellationToken cancellation)
    {
        try
        {
            return prepare(cancellation).AsTask();
        }
        catch (Exception error)
        {
            return Task.FromException<IAsyncDisposable>(error);
        }
    }

    private void Check() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void Admit()
    {
        Check();
        if (_stopping)
        {
            throw new InvalidOperationException("Popup manager is stopping.");
        }
    }

    private void Enter()
    {
        if (_busy)
        {
            throw new InvalidOperationException("Advance and Dispatch cannot be reentered.");
        }

        _busy = true;
    }

    private UiPopupHandle Enqueue(Owner owner, Popup? parent, UiPopupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (parent?.Depth >= 8 || (request.InputMode == UiPopupInputMode.Passive && _popups.Values.Count(p => p.Mode == UiPopupInputMode.Passive) >= 8))
        {
            throw new InvalidOperationException("Popup capacity exceeded.");
        }

        UiSurface anchorSurface = parent?.Surface ?? owner.Surface;
        if (!ReferenceEquals(request.Anchor.Surface, anchorSurface))
        {
            throw new ArgumentException("Anchor must belong to the logical parent surface.", nameof(request));
        }

        UiPopupHandle? handle = null;
        handle = new(() => _commands.Enqueue(() =>
        {
            if (_popups.TryGetValue(handle!.Id, out Popup? p))
            {
                Close(p, UiPopupCloseReason.Requested);
            }
        }));
        var popup = new Popup(handle, owner, parent, request, request.Anchor.AutomationId);
        _popups.Add(handle.Id, popup);
        _commands.Enqueue(() =>
        {
            popup.Accepted = true;
            if (popup.Mode == UiPopupInputMode.Menu)
            {
                foreach (Popup previous in _popups.Values.Where(p => p != popup && p.Accepted && p.Mode == UiPopupInputMode.Menu && (parent is null ? p.Parent is null && SameScope(p.Owner, owner) : p.Parent == parent)).ToArray())
                {
                    Close(previous, UiPopupCloseReason.Replaced);
                }
            }
        });
        return handle;
    }

    private bool SameScope(Owner a, Owner b) => _windows.TryGetSnapshot(a.Window, out UiWindowSnapshot? sa) && _windows.TryGetSnapshot(b.Window, out UiWindowSnapshot? sb) && sa!.KeyboardScopeId == sb!.KeyboardScopeId;

    private void ReleaseOwner(Guid id)
    {
        if (!_owners.TryGetValue(id, out Owner? owner))
        {
            return;
        }

        owner.Releasing = true;
        foreach (Popup popup in _popups.Values.Where(p => p.Owner == owner).ToArray())
        {
            Close(popup, _stopping ? UiPopupCloseReason.Shutdown : UiPopupCloseReason.OwnerUnavailable);
        }
    }

    private void Close(Popup p, UiPopupCloseReason reason)
    {
        if (p.Closing)
        {
            return;
        }

        p.Reason = reason;
        p.Handle.State = UiPopupState.Closing;
        p.Surface?.SetAnimationUpdatesEnabled(false);
        p.GuardCancellation.Cancel();
        foreach (Popup child in _popups.Values.Where(child => child.Parent == p).ToArray())
        {
            Close(child, reason);
        }
    }

    private bool Anchor(Popup p, out Rect2D rectangle)
    {
        rectangle = default;
        UiPopupRequest r = p.Request!;
        UiElement? anchor = r.ResolveAnchor is { } resolve ? resolve() : r.Anchor;
        if (anchor is null)
        {
            return false;
        }

        r.Anchor = anchor;
        UiSurface expected = p.Parent?.Surface ?? p.Owner.Surface;
        if (!ReferenceEquals(anchor.Surface, expected) || (r.ResolveAnchor is null && anchor.AutomationId != p.AnchorId) || !ReferenceEquals(anchor.InputVersion, expected.InputVersionForWindowing) || !anchor.Widget.IsVisible || !anchor.Widget.IsEnabled || anchor.InputBounds.Right <= anchor.InputBounds.Left || anchor.InputBounds.Bottom <= anchor.InputBounds.Top)
        {
            return false;
        }

        rectangle = new(anchor.InputOrigin.X, anchor.InputOrigin.Y, anchor.Size.X, anchor.Size.Y);
        return true;
    }

    private void Step(Popup p)
    {
        if (!p.Accepted)
        {
            return;
        }

        if (!_windows.TryGetSnapshot(p.Owner.Window, out UiWindowSnapshot? owner) || owner!.State is UiWindowState.Closing or UiWindowState.Closed or UiWindowState.Failed || !owner.IsVisible || !owner.IsActive || owner.ClientSize.X <= 0 || owner.ClientSize.Y <= 0)
        {
            Close(p, UiPopupCloseReason.OwnerUnavailable);
        }

        if (!p.Closing && !Anchor(p, out _))
        {
            Close(p, UiPopupCloseReason.AnchorUnavailable);
        }

        if (p.Closing)
        {
            Drain(p);
            return;
        }

        if (p.Handle.State == UiPopupState.Pending)
        {
            if (_popups.Values.Any(other => other != p && other.Closing && other.Mode == UiPopupInputMode.Menu && (p.Parent is null ? other.Parent is null && SameScope(other.Owner, p.Owner) : other.Parent == p.Parent)))
            {
                return;
            }

            if (p.Mode == UiPopupInputMode.Menu)
            {
                if (!p.GuardsStarted)
                {
                    p.GuardsStarted = true;
                    p.GuardTasks = _menuGuards.GetValueOrDefault(p.Owner.Token.Id, []).ToArray().Select(prepare => PrepareGuard(prepare, p.GuardCancellation.Token)).ToArray();
                }

                if (p.GuardTasks is { } guards)
                {
                    if (guards.Any(task => !task.IsCompleted))
                    {
                        return;
                    }

                    if (guards.Any(task => !task.IsCompletedSuccessfully))
                    {
                        p.Handle.FailureCode = "menu-guard-failed";
                        Close(p, UiPopupCloseReason.OpenFailed);
                        return;
                    }

                    p.MenuGuards.AddRange(guards.Select(task => task.GetAwaiter().GetResult()));
                    p.GuardTasks = null;
                }
            }

            Widget content;
            try
            {
                UiPopupRequest request = p.Request!;
                content = request.PreparedSurface is null ? UiSurface.CaptureBuild(request.CreateContent) : request.CreateContent();
                if (request.PreparedSurface is { } prepared && !ReferenceEquals(prepared.Root, content))
                {
                    throw new InvalidOperationException("Prepared popup content must match the accepted declaration.");
                }

                p.Surface = request.PreparedSurface ?? new(content);
                request.PreparedSurface = null;
                p.SurfaceId = p.Surface.CaptureAutomationSnapshot().SurfaceId;
            }
            catch (Exception)
            {
                p.Handle.FailureCode = "invalid-content";
                Close(p, UiPopupCloseReason.OpenFailed);
                return;
            }

            Anchor(p, out Rect2D at);
            UiWindowId anchor = p.Parent?.Window ?? p.Owner.Window;
            var placement = new UiWindowPlacement(anchor, at, p.Request!.Side, p.Request.Gap) { AlignEnd = p.Request.AlignEnd };
            p.LastPlacement = placement;
            try
            {
                p.Window = _windows.Create(new(anchor, p.Request.ClientSize, p.Request.Role, p.Mode == UiPopupInputMode.Passive ? UiWindowInputMode.Passive : UiWindowInputMode.Interactive, placement, p.Request.Policy));
                p.BindingTask = _windows.BindPresentationAsync(p.Window.Value, p.Surface).AsTask();
                p.Handle.State = UiPopupState.Opening;
            }
            catch (Exception error)
            {
                p.Handle.FailureCode = error is UiWindowOperationException operation ? operation.Code : "presentation-failed";
                Close(p, UiPopupCloseReason.OpenFailed);
            }

            return;
        }

        if (p.Handle.State == UiPopupState.Opening && p.BindingTask is { IsCompleted: true } binding)
        {
            p.BindingTask = null;
            try
            {
                p.Binding = binding.GetAwaiter().GetResult();
                p.Show = _windows.Show(p.Window!.Value);
            }
            catch (Exception error)
            {
                p.Handle.FailureCode = error is UiWindowOperationException operation ? operation.Code : "presentation-failed";
                Close(p, UiPopupCloseReason.OpenFailed);
                return;
            }
        }

        if (p.Window is not { } window || !_windows.TryGetSnapshot(window, out UiWindowSnapshot? state))
        {
            return;
        }

        p.Handle.Presentation = state!.Presentation;
        p.Handle.FallbackCode = state.FallbackCode;
        if (state.State is UiWindowState.Failed or UiWindowState.Closed or UiWindowState.Closing)
        {
            p.Handle.FailureCode = state.FailureCode;
            Close(p, p.Handle.State == UiPopupState.Open ? UiPopupCloseReason.PresenterLost : UiPopupCloseReason.OpenFailed);
            return;
        }

        if (p.Handle.State == UiPopupState.Opening && p.Show is { } show && _windows.TryGetRequest(show, out UiWindowRequestResult? shown) && shown!.Status == UiWindowRequestStatus.Succeeded && state.State == UiWindowState.Open && state.IsVisible)
        {
            if (p.Mode == UiPopupInputMode.Menu)
            {
                p.Previous = p.Parent?.Surface ?? p.Owner.Surface;
                p.Bookmark = p.Previous.CaptureFocus();
                CancelWindow(p.Parent?.Window ?? p.Owner.Window, p.Previous);
                p.Surface!.MoveFocus(UiFocusNavigation.First);
            }

            p.Handle.State = UiPopupState.Open;
            if (p.Request!.OnOpened is { } callback)
            {
                var opened = new UiPopupOpened(p.Handle.Id, state.Presentation!.Value, state.FallbackCode);
                _callbacks.Enqueue(() => callback(opened));
            }
        }

        if (p.Handle.State == UiPopupState.Open && Anchor(p, out Rect2D rect))
        {
            var placement = new UiWindowPlacement(p.Parent?.Window ?? p.Owner.Window, rect, p.Request!.Side, p.Request.Gap) { AlignEnd = p.Request.AlignEnd };
            if (placement != p.LastPlacement)
            {
                _windows.Place(window, placement);
                p.LastPlacement = placement;
            }
        }
    }

    private void Drain(Popup p)
    {
        if (_popups.Values.Any(child => child.Parent == p))
        {
            return;
        }

        if (p.GuardTasks is { } guards)
        {
            if (guards.Any(task => !task.IsCompleted))
            {
                return;
            }

            foreach (Task<IAsyncDisposable> task in guards)
            {
                try
                {
                    p.MenuGuards.Add(task.GetAwaiter().GetResult());
                }
                catch (Exception error)
                {
                    p.Failures.Add(error);
                }
            }

            p.GuardTasks = null;
        }

        if (!p.InputCanceled)
        {
            p.InputCanceled = true;
            if (p.Surface is not null && p.Window is { } w)
            {
                CancelWindow(w, p.Surface);
            }
        }

        if (p.Cleanup is null)
        {
            p.Cleanup = p.Participants.Select(participant => DisposeParticipant(participant)).ToArray();
            p.Participants.Clear();
        }

        if (p.Cleanup.Any(task => !task.IsCompleted))
        {
            return;
        }

        List<Exception> failures = p.Failures;
        foreach (Task task in p.Cleanup)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        p.Cleanup = [];
        if (p.BindingTask is { } pending)
        {
            // Close cancels preparation; the manager still owns all partial resources until the task settles.
            if (p.Window is { } preparing)
            {
                p.CloseRequest ??= _windows.Close(preparing);
            }

            if (!pending.IsCompleted)
            {
                return;
            }

            p.BindingTask = null;
            try
            {
                p.Binding = pending.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        if (p.Binding is not null && p.Retirement is null)
        {
            p.Retirement = p.Binding.DisposeAsync().AsTask();
        }

        if (p.Retirement is { IsCompleted: false })
        {
            return;
        }

        if (p.Retirement is not null)
        {
            try
            {
                p.Retirement.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }

            p.Retirement = null;
            p.Binding = null;
        }

        p.Surface?.Dispose();
        p.Surface = null;
        if (p.Request?.PreparedSurface is { } unadopted)
        {
            unadopted.Dispose();
            p.Request.PreparedSurface = null;
        }

        if (p.Window is { } window)
        {
            p.CloseRequest ??= _windows.Close(window);
            if (!_windows.TryGetSnapshot(window, out UiWindowSnapshot? snapshot) || snapshot!.State is not (UiWindowState.Closed or UiWindowState.Failed))
            {
                return;
            }

            _windows.Release(window);
            p.Window = null;
        }

        p.GuardRelease ??= p.MenuGuards.Select(DisposeParticipant).ToArray();
        p.MenuGuards.Clear();
        if (p.GuardRelease.Any(task => !task.IsCompleted))
        {
            return;
        }

        foreach (Task released in p.GuardRelease)
        {
            try
            {
                released.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        p.GuardRelease = [];
        p.GuardCancellation.Dispose();

        if (!p.SuppressRestore && p.Previous is not null && p.Bookmark is not null && !p.Owner.Releasing && SameLiveOwner(p.Owner))
        {
            if (!p.Previous.TryRestoreFocus(p.Bookmark))
            {
                p.Previous.Dispatch(new UiFocusLostInput());
            }
        }

        p.Previous = null;
        p.Bookmark = null;
        if (failures.Count != 0)
        {
            p.Handle.FailureCode = "cleanup-failed";
        }

        p.Handle.State = p.Reason == UiPopupCloseReason.OpenFailed ? UiPopupState.Failed : UiPopupState.Closed;
        Action<UiPopupClosed>? callback = p.Request!.OnClosed;
        p.Request = null;
        _popups.Remove(p.Handle.Id);
        if (callback is not null)
        {
            var closed = new UiPopupClosed(p.Handle.Id, p.Reason, p.Handle.FailureCode);
            _callbacks.Enqueue(() => callback(closed));
        }

        if (failures.Count != 0)
        {
            throw new AggregateException(failures);
        }
    }

    private bool SameLiveOwner(Owner owner) => _windows.TryGetSnapshot(owner.Window, out UiWindowSnapshot? s) && s!.IsActive && s.IsVisible && s.State == UiWindowState.Open;

    private Popup? Deepest(Guid scope) => _popups.Values.Where(p => p.Accepted && p.Mode == UiPopupInputMode.Menu && _windows.TryGetSnapshot(p.Owner.Window, out UiWindowSnapshot? s) && s!.KeyboardScopeId == scope).OrderByDescending(p => p.Depth).ThenByDescending(p => p.Order).FirstOrDefault();

    private UiSurface? Surface(UiWindowId window) => _candidateSurfaces.GetValueOrDefault(window) ?? _owners.Values.FirstOrDefault(o => o.Window == window && !o.Paused)?.Surface ?? _popups.Values.FirstOrDefault(p => p.Window == window && !p.Closing)?.Surface;

    private void CancelWindow(UiWindowId window, UiSurface surface)
    {
        foreach (long pointer in _pointers.Where(pair => pair.Value == window).Select(pair => pair.Key).ToArray())
        {
            _pointers[pointer] = null;
        }

        foreach ((InputDeviceId? Device, Key Key) key in _keys.Where(pair => pair.Value == window).Select(pair => pair.Key).ToArray())
        {
            _keys[key] = null;
        }

        surface.Dispatch(new UiFocusLostInput());
    }

    private bool Route(UiWindowInputEnvelope envelope)
    {
        UiInput input = envelope.Input ?? throw new ArgumentNullException(nameof(envelope));
        UiWindowId? root = _windows.ResolveInputRoot(envelope.Origin);
        if (input is UiPointerInput pointer && _pointers.TryGetValue(pointer.PointerId, out UiWindowId? held))
        {
            if (pointer.Phase is UiPointerPhase.Up or UiPointerPhase.Cancel)
            {
                _pointers.Remove(pointer.PointerId);
            }

            if (held is { } target && Surface(target) is { } surface)
            {
                if (root is { } origin && _windows.TryTransformPoint(origin, target, pointer.Position, out Vector2 at))
                {
                    surface.Dispatch(pointer with { Position = at, });
                }
                else
                {
                    surface.Dispatch(pointer with { Phase = UiPointerPhase.Cancel, Position = Vector2.Zero, });
                    if (pointer.Phase is not (UiPointerPhase.Up or UiPointerPhase.Cancel))
                    {
                        _pointers[pointer.PointerId] = null;
                    }
                }
            }

            return true;
        }

        if (input is UiKeyCancelInput canceled && _keys.Remove((envelope.Device, canceled.Key), out UiWindowId? canceledOwner))
        {
            if (canceledOwner is { } target && Surface(target) is { } surface)
            {
                surface.Dispatch(canceled);
            }

            return true;
        }

        if (input is UiKeyInput key && _keys.TryGetValue((envelope.Device, key.Key), out UiWindowId? keyOwner))
        {
            if (!key.IsDown)
            {
                _keys.Remove((envelope.Device, key.Key));
            }

            if (keyOwner is { } target && Surface(target) is { } surface)
            {
                surface.Dispatch(key);
            }

            return true;
        }

        if (root is not { } originRoot || !_windows.TryGetSnapshot(originRoot, out UiWindowSnapshot? rootState))
        {
            return false;
        }

        if (input is UiFocusLostInput)
        {
            foreach (Owner owner in _owners.Values.Where(o => SameScopeRoot(o, rootState!)).ToArray())
            {
                NotifyOutsidePress(owner.Token);
                CancelWindow(owner.Window, owner.Surface);
            }

            return true;
        }

        Popup? menu = Deepest(rootState!.KeyboardScopeId);
        if (input is UiPointerInput point)
        {
            UiWindowHit? hit = _windows.HitTest(originRoot, point.Position);
            if (hit?.InputMode == UiWindowInputMode.Candidate)
            {
                UiWindowId candidateWindow = hit.Value.Window;
                if (_candidateSurfaces.TryGetValue(candidateWindow, out UiSurface? candidate) && _windows.TryTransformPoint(originRoot, candidateWindow, point.Position, out Vector2 candidatePoint))
                {
                    candidate.Dispatch(point with { Position = candidatePoint });
                    if (point.Phase == UiPointerPhase.Down)
                    {
                        _pointers[point.PointerId] = candidateWindow;
                    }
                }
                else if (point.Phase == UiPointerPhase.Down)
                {
                    _pointers[point.PointerId] = null;
                }

                return true;
            }

            Popup? hitPopup = hit is { } h ? _popups.Values.FirstOrDefault(p => p.Window == h.Window && !p.Closing && p.Mode == UiPopupInputMode.Menu && p.Handle.State == UiPopupState.Open) : null;
            if (menu is not null && menu.Handle.State != UiPopupState.Open)
            {
                if (point.Phase == UiPointerPhase.Down)
                {
                    _pointers[point.PointerId] = null;
                }

                return true;
            }

            if (menu is not null && hitPopup is null)
            {
                if (point.Phase == UiPointerPhase.Down)
                {
                    CloseChain(menu, UiPopupCloseReason.OutsidePress);
                    _pointers[point.PointerId] = null;
                }

                return true;
            }

            if (point.Phase == UiPointerPhase.Down && hitPopup is not null)
            {
                foreach (Popup child in _popups.Values.Where(p => p.Parent == hitPopup).ToArray())
                {
                    child.SuppressRestore = true;
                    Close(child, UiPopupCloseReason.OutsidePress);
                }
            }

            if (hit is not { } destination || Surface(destination.Window) is not { } targetSurface)
            {
                return menu is not null;
            }

            if (point.Phase == UiPointerPhase.Down && menu is null && _windows.TryGetSnapshot(destination.Window, out UiWindowSnapshot? normal) && normal!.Role is UiWindowRole.Normal or UiWindowRole.Tool)
            {
                UiWindowId? previous = _windows.GetKeyboardTarget(originRoot);
                if (previous != destination.Window && previous is { } old && Surface(old) is { } oldSurface)
                {
                    CancelWindow(old, oldSurface);
                }

                _windows.SetKeyboardTarget(destination.Window);
            }

            bool pointerHandled = targetSurface.Dispatch(point with { Position = destination.LocalPosition, }) || menu is not null;
            if (point.Phase == UiPointerPhase.Down && pointerHandled)
            {
                _pointers[point.PointerId] = destination.Window;
            }

            return pointerHandled;
        }

        if (menu is not null && menu.Handle.State != UiPopupState.Open)
        {
            if (input is UiKeyInput { IsDown: true } pendingKey)
            {
                _keys[(envelope.Device, pendingKey.Key)] = null;
            }

            return true;
        }

        UiWindowId? keyTarget = menu?.Window ?? _windows.GetKeyboardTarget(originRoot);
        if (keyTarget is not { } selected || Surface(selected) is not { } selectedSurface)
        {
            return menu is not null;
        }

        bool handled = selectedSurface.Dispatch(input);
        if (input is UiKeyInput pressed && pressed.IsDown && (handled || menu is not null))
        {
            _keys[(envelope.Device, pressed.Key)] = selected;
        }

        if (!handled && menu is not null && input is UiKeyInput { IsDown: true } navigation)
        {
            UiFocusNavigation? direction = navigation.Key switch
            {
                Key.ArrowDown => UiFocusNavigation.Next,
                Key.ArrowUp => UiFocusNavigation.Previous,
                Key.Home => UiFocusNavigation.First,
                Key.End => UiFocusNavigation.Last,
                _ => null,
            };
            if (direction is { } move)
            {
                selectedSurface.MoveFocus(move);
                _keys[(envelope.Device, navigation.Key)] = null;
            }
            else if (navigation.Key == Key.Escape || (navigation.Key == Key.ArrowLeft && menu.Parent is not null))
            {
                RequestDismiss(menu, UiPopupCloseReason.Escape);
                _keys[(envelope.Device, navigation.Key)] = null;
            }
        }

        return handled || menu is not null;
    }

    private bool SameScopeRoot(Owner owner, UiWindowSnapshot root) => _windows.TryGetSnapshot(owner.Window, out UiWindowSnapshot? s) && s!.KeyboardScopeId == root.KeyboardScopeId;

    private void CloseChain(Popup p, UiPopupCloseReason reason)
    {
        while (p.Parent is not null)
        {
            p = p.Parent;
        }

        RequestDismiss(p, reason);
    }

    private void RequestDismiss(Popup popup, UiPopupCloseReason reason)
    {
        if (popup.Request?.OnDismissRequested is { } notify)
        {
            _callbacks.Enqueue(() => notify(new(reason == UiPopupCloseReason.Escape ? PopupDismissReason.Escape : PopupDismissReason.OutsidePress)));
        }
        else
        {
            Close(popup, reason);
        }
    }

    private sealed class InputRegistration(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class Popup(UiPopupHandle handle, Owner owner, Popup? parent, UiPopupRequest request, UiAutomationId anchorId)
    {
        internal CancellationTokenSource GuardCancellation { get; } = new();

        internal List<IAsyncDisposable> MenuGuards { get; } = [];

        internal bool GuardsStarted { get; set; }

        internal Task<IAsyncDisposable>[]? GuardTasks { get; set; }

        internal Task[]? GuardRelease { get; set; }

        internal UiPopupHandle Handle { get; } = handle;

        internal Owner Owner { get; } = owner;

        internal Popup? Parent { get; } = parent;

        internal UiPopupInputMode Mode { get; } = request.InputMode;

        internal UiAutomationId AnchorId { get; } = anchorId;

        internal int Depth { get; } = (parent?.Depth ?? 0) + 1;

        internal long Order { get; } = System.Diagnostics.Stopwatch.GetTimestamp();

        internal UiPopupRequest? Request { get; set; } = request;

        internal bool Accepted { get; set; }

        internal bool Closing => Handle.State is UiPopupState.Closing or UiPopupState.Closed or UiPopupState.Failed;

        internal bool InputCanceled { get; set; }

        internal bool SuppressRestore { get; set; }

        internal UiPopupCloseReason Reason { get; set; }

        internal Guid SurfaceId { get; set; }

        internal UiSurface? Surface { get; set; }

        internal UiSurface? Previous { get; set; }

        internal UiFocusBookmark? Bookmark { get; set; }

        internal UiWindowId? Window { get; set; }

        internal UiWindowRequestId? Show { get; set; }

        internal UiWindowRequestId? CloseRequest { get; set; }

        internal UiWindowPlacement? LastPlacement { get; set; }

        internal Task<UiWindowBinding>? BindingTask { get; set; }

        internal UiWindowBinding? Binding { get; set; }

        internal Task? Retirement { get; set; }

        internal List<IAsyncDisposable> Participants { get; } = [];

        internal Task[]? Cleanup { get; set; }

        internal List<Exception> Failures { get; } = [];
    }
}
