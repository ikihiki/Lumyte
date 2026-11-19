using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Dialog = Lumyte.UI.Windowing.Compose.Definitions.Dialog;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing;

/// <summary>Drives declarative dialogs on the borrowed manager owner; disposal starts an ordered drain.</summary>
public sealed class UiDialogPortalCoordinator : IDisposable
{
    private readonly IUiWindowManager _windows;
    private readonly UiPopupManager _popups;
    private readonly UiPopupOwner _owner;
    private readonly UiWindowId _ownerWindow;
    private readonly UiSurface _surface;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private DialogElement[] _portals = [];
    private ulong _structureRevision;
    private bool _indexed;
    private bool _disposed;
    private bool _advancing;
    private bool _enabled = true;

    /// <summary>Initializes a new instance of the <see cref="UiDialogPortalCoordinator"/> class.</summary>
    /// <param name="windows">The borrowed window manager.</param>
    /// <param name="popups">Its borrowed input router.</param>
    /// <param name="owner">The registered logical owner.</param>
    /// <param name="ownerWindow">The bound owner window.</param>
    /// <param name="surface">The same owner surface.</param>
    public UiDialogPortalCoordinator(IUiWindowManager windows, UiPopupManager popups, UiPopupOwner owner, UiWindowId ownerWindow, UiSurface surface)
        : this(windows, popups, owner, ownerWindow, surface, false)
    {
    }

    internal UiDialogPortalCoordinator(IUiWindowManager windows, UiPopupManager popups, UiPopupOwner owner, UiWindowId ownerWindow, UiSurface surface, bool staged)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(popups);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(surface);
        if (ownerWindow.ManagerId != windows.Id || (!staged && !windows.IsBound(ownerWindow, surface)))
        {
            throw new ArgumentException("The dialog owner must already be bound.");
        }

        _windows = windows;
        _popups = popups;
        _owner = owner;
        _ownerWindow = ownerWindow;
        _surface = surface;
        _enabled = !staged;
    }

    /// <summary>Gets a value indicating whether a disposed coordinator has completely drained.</summary>
    public bool IsStopped => _disposed && _sessions.Count == 0;

    /// <summary>Captures open dialog identities without exposing mutable declarations.</summary>
    /// <returns>A bounded detached snapshot, including nested dialogs.</returns>
    public IReadOnlyList<UiDialogOpened> CaptureDialogs()
    {
        Check();
        return Array.AsReadOnly(_sessions.Values
        .Where(session => session.Opened && !session.Closing && session.Window is not null)
        .Select(session => Opened(session))
        .Concat(_sessions.Values.SelectMany(session => session.Dialogs?.CaptureDialogs() ?? Array.Empty<UiDialogOpened>())).ToArray());
    }

    /// <summary>Borrows one current dialog surface on the owner thread.</summary>
    /// <param name="id">The nonreused session identity.</param>
    /// <param name="surface">The borrowed open surface.</param>
    /// <returns>Whether the dialog remains open.</returns>
    public bool TryGetSurface(Guid id, out UiSurface? surface)
    {
        Check();
        foreach (Session session in _sessions.Values)
        {
            if (session.Id == id && session.Opened && !session.Closing)
            {
                surface = session.Surface;
                return surface is not null;
            }

            if (session.Dialogs?.TryGetSurface(id, out surface) == true)
            {
                return true;
            }
        }

        surface = null;
        return false;
    }

    /// <summary>Transfers asynchronous text, automation or visual cleanup into the dialog close drain.</summary>
    /// <param name="id">An open session identity.</param>
    /// <param name="participant">A unique owned cleanup participant.</param>
    public void BindLifetime(Guid id, IAsyncDisposable participant)
    {
        Check();
        ArgumentNullException.ThrowIfNull(participant);
        foreach (Session session in _sessions.Values)
        {
            if (session.Id == id && session.Opened && !session.Closing)
            {
                if (session.Participants.Contains(participant, ReferenceEqualityComparer.Instance))
                {
                    throw new ArgumentException("A cleanup participant can be bound only once.", nameof(participant));
                }

                session.Participants.Add(participant);
                return;
            }

            if (session.Dialogs?.CaptureDialogs().Any(dialog => dialog.DialogId == id) == true)
            {
                session.Dialogs.BindLifetime(id, participant);
                return;
            }
        }

        throw new InvalidOperationException("dialog-unavailable");
    }

    /// <summary>Reconciles model intent and observes completed tasks at an explicit owner-loop boundary.</summary>
    public void Advance()
    {
        Check();
        if (_advancing)
        {
            throw new InvalidOperationException("Dialog advancement cannot reenter.");
        }

        _advancing = true;
        var callbacks = new List<Action>();
        var errors = new List<Exception>();
        try
        {
            if (!_disposed && _enabled && !_windows.IsBound(_ownerWindow, _surface))
            {
                throw new InvalidOperationException("The dialog owner is not the currently committed bound surface.");
            }

            DialogElement[] portals = _disposed || !_enabled ? [] : ResolvePortals();
            if (portals.Length > 16 || portals.Select(element => element.Widget.Key).Distinct(StringComparer.Ordinal).Count() != portals.Length)
            {
                throw new InvalidOperationException("Dialog keys must be unique; at most sixteen declarations are admitted per surface.");
            }

            Dictionary<string, DialogElement> current = portals.ToDictionary(element => element.Widget.Key!, StringComparer.Ordinal);
            foreach ((string key, Session session) in _sessions.ToArray())
            {
                if (!current.TryGetValue(key, out DialogElement? element) || !ReferenceEquals(element, session.Portal) || !element.Widget.IsOpen || _disposed)
                {
                    Close(session, _disposed ? "stopped" : element is null || !ReferenceEquals(element, session.Portal) ? "removed" : "model-closed");
                }
                else
                {
                    Dialog previous = session.Declaration;
                    session.Declaration = element.Widget;
                    if (previous.Presentation != element.Widget.Presentation || previous.IsModal != element.Widget.IsModal)
                    {
                        Close(session, "policy-changed");
                    }
                    else if (!session.Closing && session.Surface is { } content && !ReferenceEquals(previous, element.Widget))
                    {
                        try
                        {
                            content.Rebuild(BuildContent(element.Widget));
                            content.UpdateTree();
                        }
                        catch (Exception)
                        {
                            Close(session, "invalid-content");
                        }
                    }
                }
            }

            foreach (DialogElement element in portals)
            {
                string key = element.Widget.Key!;
                if (!element.Widget.IsOpen || _sessions.ContainsKey(key) || !OwnerAvailable())
                {
                    continue;
                }

                var session = new Session(element);
                _sessions.Add(key, session);

                // Reserve the identity before content factories, guards or user callbacks can reenter.
                if (element.Widget.Presentation == DialogPresentation.NativeRequired)
                {
                    session.Failed = true;
                    session.Terminal = true;
                    session.Notified = true;
                    session.Cancellation.Dispose();
                    session.Code = "native-unavailable";
                    callbacks.Add(() => session.Declaration.OnClosed?.Invoke(Closed(session)));
                    continue;
                }

                try
                {
                    Open(session);
                }
                catch (Exception)
                {
                    Close(session, "open-failed");
                }
            }

            foreach ((string key, Session session) in _sessions.ToArray())
            {
                try
                {
                    if (session.Failed && !session.Closing)
                    {
                        continue;
                    }

                    if (session.Closing)
                    {
                        if (Drain(session))
                        {
                            bool failed = Closed(session).Reason == UiDialogCloseReason.Failed;
                            if (failed && !session.ResetRequested && !_disposed && current.TryGetValue(key, out DialogElement? live) && ReferenceEquals(live, session.Portal) && live.Widget.IsOpen)
                            {
                                session.Failed = true;
                                session.Closing = false;
                            }
                            else
                            {
                                _sessions.Remove(key);
                            }

                            if (!session.Notified)
                            {
                                session.Notified = true;
                                callbacks.Add(() => session.Declaration.OnClosed?.Invoke(Closed(session)));
                            }
                        }
                    }
                    else
                    {
                        Step(session, callbacks);
                    }
                }
                catch (Exception)
                {
                    Close(session, "presentation-failed");
                }
            }
        }
        finally
        {
            _advancing = false;
        }

        foreach (Action callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException(errors);
        }
    }

    /// <summary>Stops admission. Continue Advance while the borrowed manager and popup router drain.</summary>
    public void Dispose()
    {
        Check();
        _disposed = true;
        foreach (Session session in _sessions.Values.ToArray())
        {
            Close(session, "stopped");
        }
    }

    internal void SetUpdatesEnabled(bool enabled) => _enabled = enabled;

    internal IDisposable RegisterTextGuard(Guid id, Func<CancellationToken, ValueTask<IAsyncDisposable>> prepare)
    {
        Check();
        foreach (Session session in _sessions.Values)
        {
            if (session.Id == id && session.Opened && !session.Closing && session.Owner is { } owner)
            {
                return _popups.RegisterMenuGuard(owner, prepare);
            }

            if (session.Dialogs?.CaptureDialogs().Any(dialog => dialog.DialogId == id) == true)
            {
                return session.Dialogs.RegisterTextGuard(id, prepare);
            }
        }

        throw new InvalidOperationException("dialog-unavailable");
    }

    private static UiDialogCloseReason ClosedReason(Session session) => session.Code switch
    {
        "model-closed" or "policy-changed" => UiDialogCloseReason.ModelClosed,
        "removed" => UiDialogCloseReason.Removed,
        "owner-closed" or "owner-unavailable" => UiDialogCloseReason.OwnerClosed,
        "stopped" => UiDialogCloseReason.Stopped,
        _ => UiDialogCloseReason.Failed,
    };

    private static UiDialogClosed Closed(Session session) => new(session.Id, ClosedReason(session), session.Code, session.Opened ? UiWindowPresentation.Hosted : null);

    private static UiDialogOpened Opened(Session session) => new(session.Id, session.Window!.Value, UiWindowPresentation.Hosted, session.Declaration.Presentation == DialogPresentation.Automatic ? "native-unavailable" : string.Empty);

    private static bool Visible(DialogElement element)
    {
        for (UiElement? node = element; node is not null; node = node.Parent)
        {
            if (!node.LayoutVisible)
            {
                return false;
            }
        }

        return true;
    }

    private static Widget BuildContent(Dialog declaration)
    {
        UiDeclarationRecipe[] recipes = declaration.Content.Select(child => UiDeclarationRecipe.Capture(child)
            ?? throw new InvalidOperationException("Dialog content requires generated Composition construction recipes.")).ToArray();
        return Lumyte.UI.Compose.Panel(automationName: declaration.Title)[recipes.Select(recipe => recipe.Build()).ToArray()];
    }

    private static Task Release(IAsyncDisposable participant)
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

    private static void InitiateClose(Action action, Session session)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            session.Code = "cleanup-failed";
        }
    }

    private DialogElement[] ResolvePortals()
    {
        if (!_indexed || _structureRevision != _surface.StructureRevision)
        {
            _portals = UiSurface.CollectElements(_surface.RootElement).OfType<DialogElement>().ToArray();
            _structureRevision = _surface.StructureRevision;
            _indexed = true;
        }

        return _portals.Length == 0 ? [] : _portals.Where(Visible).ToArray();
    }

    private bool OwnerAvailable() => _windows.TryGetSnapshot(_ownerWindow, out UiWindowSnapshot? owner)
        && owner is { IsVisible: true, State: UiWindowState.Open };

    private long? KeyboardGeneration() => _windows switch
    {
        HostedUiWindowManager hosted => hosted.KeyboardTargetGeneration,
        FallbackUiWindowManager fallback => fallback.KeyboardTargetGeneration,
        UiWindowManagerCore core => core.KeyboardTargetGeneration,
        _ => null,
    };

    private void Open(Session session)
    {
        Dialog declaration = session.Declaration;
        _popups.ValidateDialogAdmission();
        if (!_windows.TryGetSnapshot(_ownerWindow, out UiWindowSnapshot? owner) || owner is not { IsVisible: true })
        {
            Close(session, "owner-unavailable");
            return;
        }

        Vector2 size = declaration.InitialClientSize;
        float x = Math.Max(0, (owner.ClientSize.X - size.X) / 2);
        float y = Math.Max(0, (owner.ClientSize.Y - size.Y) / 2);
        (session.PreviousSurface, session.PreviousWindow, session.Bookmark, session.FocusRequest) = _popups.CaptureDialogReturn(_ownerWindow, _surface);
        session.Window = _windows.Create(new(_ownerWindow, size, UiWindowRole.Normal, UiWindowInputMode.Interactive, new(_ownerWindow, new Rect2D(x, y, 0, 0), UiWindowPlacementSide.Below), UiWindowPolicy.HostedOnly));
        session.LogicalWindow = _popups.RegisterLogicalChild(session.Window.Value, _ownerWindow, reason => RequestClose(session, reason));
        if (declaration.IsModal)
        {
            session.Modal = _popups.EnterModal(session.Window.Value, _ownerWindow, escape => RequestClose(session, escape ? UiDialogCloseRequestReason.Escape : UiDialogCloseRequestReason.OutsidePress));
        }

        session.Guards = _popups.PrepareDialogGuards(_owner, session.Cancellation.Token);
    }

    private void Step(Session session, List<Action> callbacks)
    {
        if (!_windows.TryGetSnapshot(_ownerWindow, out UiWindowSnapshot? owner) || owner is not { IsVisible: true } || owner.State is UiWindowState.Closing or UiWindowState.Closed or UiWindowState.Failed)
        {
            Close(session, "owner-closed");
            return;
        }

        if (session.Surface is null)
        {
            if (session.Guards.Any(task => !task.IsCompleted) || _popups.HasClosingMenus(_ownerWindow))
            {
                return;
            }

            foreach (Task<IAsyncDisposable> guard in session.Guards)
            {
                guard.GetAwaiter().GetResult();
            }

            session.Surface = new(BuildContent(session.Declaration), independentContent: true);
            session.Surface.CloseRequested = () => RequestClose(session, UiDialogCloseRequestReason.CloseButton);
            if (session.Declaration.InitialFocus is { } initial && UiSurface.CollectElements(session.Surface.RootElement).Count(element => element.Widget.Key == initial) > 1)
            {
                throw new InvalidOperationException("Initial focus must identify at most one content element before publication.");
            }

            session.BindingTask = _windows.BindPresentationAsync(session.Window!.Value, session.Surface, session.Cancellation.Token).AsTask();
        }

        if (session.Binding is null && session.BindingTask is { IsCompleted: true } binding)
        {
            session.Binding = binding.GetAwaiter().GetResult();
            session.BindingTask = null;
            session.Surface.UpdateLayout();
            session.Owner = _popups.RegisterOwner(session.Window!.Value, session.Surface);
            session.Portals = new(_popups, session.Owner, session.Surface);
            session.Dialogs = new(_windows, _popups, session.Owner, session.Window.Value, session.Surface);
            session.Show = _windows.Show(session.Window.Value);
        }

        if (!_windows.TryGetSnapshot(session.Window!.Value, out UiWindowSnapshot? state) || state is null)
        {
            return;
        }

        if (state.State is UiWindowState.Closing or UiWindowState.Closed or UiWindowState.Failed)
        {
            Close(session, "presentation-lost");
            return;
        }

        if (!session.Opened && session.Show is { } show && _windows.TryGetRequest(show, out UiWindowRequestResult? shown) && shown!.Status == UiWindowRequestStatus.Succeeded && state.IsVisible)
        {
            session.Opened = true;
            _windows.SetKeyboardTarget(session.Window.Value);
            session.KeyboardGeneration = KeyboardGeneration();
            UiElement[] candidates = UiSurface.CollectElements(session.Surface.RootElement).Where(element => element.Widget.Key == session.Declaration.InitialFocus).ToArray();
            if (session.Declaration.InitialFocus is not null && candidates.Length > 1)
            {
                throw new InvalidOperationException("Initial focus must identify at most one content element.");
            }

            if (session.Declaration.InitialFocus is null || candidates.Length == 0 || !session.Surface.TryFocus(candidates[0]))
            {
                session.Surface.MoveFocus(UiFocusNavigation.First);
            }

            callbacks.Add(() =>
            {
                if (!session.Closing)
                {
                    session.Declaration.OnOpened?.Invoke(Opened(session));
                }
            });
        }

        if (session.Opened)
        {
            if (!session.Declaration.IsModal)
            {
                session.GuardRelease ??= session.Guards.Select(task => Release(task.GetAwaiter().GetResult())).ToArray();
                foreach (Task release in session.GuardRelease.Where(task => task.IsCompleted))
                {
                    release.GetAwaiter().GetResult();
                }
            }

            session.Surface.UpdateLayout();
            session.Portals?.Advance();
            session.Dialogs?.Advance();
            if (_windows.GetKeyboardTarget(session.Window!.Value) == session.Window)
            {
                session.KeyboardGeneration = KeyboardGeneration();
            }
        }
    }

    private void RequestClose(Session session, UiDialogCloseRequestReason reason)
    {
        if (!session.Opened || session.Closing || (reason == UiDialogCloseRequestReason.Escape && !session.Declaration.CloseOnEscape)
            || (reason == UiDialogCloseRequestReason.OutsidePress && !session.Declaration.CloseOnOutsidePress))
        {
            return;
        }

        session.Declaration.OnCloseRequested?.Invoke(new(session.Id, reason));
    }

    private void Close(Session session, string code)
    {
        if (session.Closing)
        {
            return;
        }

        session.Closing = true;
        session.ResetRequested = code is "policy-changed" or "model-closed" or "removed" or "stopped";
        if (session.Terminal)
        {
            return;
        }

        session.Code = code;
        InitiateClose(() => session.Cancellation.Cancel(), session);
        InitiateClose(() => session.Portals?.Dispose(), session);
        InitiateClose(() => session.Dialogs?.Dispose(), session);
        InitiateClose(() => session.Owner?.Dispose(), session);
        try
        {
            session.Surface?.Dispatch(new UiFocusLostInput());
        }
        catch (Exception)
        {
            session.Code = "cleanup-failed";
        }
    }

    private bool Drain(Session session)
    {
        if (session.Terminal)
        {
            return true;
        }

        session.Dialogs?.Advance();
        if (session.Dialogs is { IsStopped: false } || session.Owner is { IsReleased: false } || session.Guards.Any(task => !task.IsCompleted))
        {
            return false;
        }

        session.Cleanup ??= session.Participants.Select(Release).ToArray();
        if (session.Cleanup.Any(task => !task.IsCompleted))
        {
            return false;
        }

        foreach (Task task in session.Cleanup)
        {
            if (!task.IsCompletedSuccessfully)
            {
                _ = task.Exception;
                session.Code = "cleanup-failed";
            }
        }

        session.Cleanup = [];
        foreach (Task<IAsyncDisposable> guard in session.Guards)
        {
            if (guard.IsFaulted)
            {
                _ = guard.Exception;
                session.Code = "guard-failed";
            }
        }

        if (session.Window is { } closing && session.Close is null)
        {
            session.Close = _windows.Close(closing);
        }

        if (session.BindingTask is { } pending)
        {
            if (!pending.IsCompleted)
            {
                return false;
            }

            session.BindingTask = null;
            if (pending.IsCompletedSuccessfully)
            {
                session.Binding = pending.GetAwaiter().GetResult();
            }
        }

        session.Retirement ??= session.Binding is null ? Task.CompletedTask : Release(session.Binding);
        if (!session.Retirement.IsCompleted)
        {
            return false;
        }

        if (!session.Retirement.IsCompletedSuccessfully)
        {
            _ = session.Retirement.Exception;
            session.Code = "cleanup-failed";
        }

        UiSurface? retiredSurface = session.Surface;
        session.Surface = null;
        try
        {
            retiredSurface?.Dispose();
        }
        catch (Exception)
        {
            session.Code = "cleanup-failed";
        }

        if (session.Window is { } window)
        {
            if (!_windows.TryGetSnapshot(window, out UiWindowSnapshot? state) || state!.State is not (UiWindowState.Closed or UiWindowState.Failed))
            {
                return false;
            }

            _windows.Release(window);
            session.Window = null;
        }

        session.GuardRelease ??= session.Guards.Where(task => task.IsCompletedSuccessfully).Select(task => Release(task.GetAwaiter().GetResult())).ToArray();
        if (session.GuardRelease.Any(task => !task.IsCompleted))
        {
            return false;
        }

        foreach (Task task in session.GuardRelease)
        {
            if (!task.IsCompletedSuccessfully)
            {
                _ = task.Exception;
                session.Code = "cleanup-failed";
            }
        }

        session.Modal?.Dispose();
        session.Modal = null;
        session.LogicalWindow?.Dispose();
        session.LogicalWindow = null;
        session.Cancellation.Dispose();
        session.Terminal = true;
        if (!_disposed && !_owner.IsReleaseRequested && session.PreviousSurface is { } previous && _popups.FindWindow(previous) == session.PreviousWindow
            && previous.FocusRequest == session.FocusRequest && session.Bookmark is { } bookmark
            && _windows.TryGetSnapshot(session.PreviousWindow, out UiWindowSnapshot? owner) && owner is { IsVisible: true, State: UiWindowState.Open }
            && ((session.KeyboardGeneration is { } generation && KeyboardGeneration() == generation)
                || (session.KeyboardGeneration is null && (_windows.GetKeyboardTarget(_ownerWindow) is not { } current || current == _ownerWindow || current == session.PreviousWindow))))
        {
            if (_popups.AllowsModal(session.PreviousWindow))
            {
                _windows.SetKeyboardTarget(session.PreviousWindow);
                if (!previous.TryRestoreFocus(bookmark) && previous.FocusRequest == session.FocusRequest)
                {
                    previous.MoveFocus(UiFocusNavigation.First);
                }
            }
            else if (_popups.CurrentModalTarget(session.PreviousWindow) is { } permitted
                && _windows.TryGetSnapshot(permitted, out UiWindowSnapshot? active) && active is { IsVisible: true, State: UiWindowState.Open }
                && _popups.BorrowRegisteredSurface(permitted) is { } modalSurface)
            {
                _windows.SetKeyboardTarget(permitted);
                if (modalSurface.FocusedElement is null)
                {
                    modalSurface.MoveFocus(UiFocusNavigation.First);
                }
            }
        }

        return true;
    }

    private void Check()
    {
        if (Environment.CurrentManagedThreadId != _thread)
        {
            throw new InvalidOperationException("Dialog coordination requires its creating owner thread.");
        }
    }

    private sealed class Session(DialogElement portal)
    {
        internal Guid Id { get; } = Guid.NewGuid();

        internal DialogElement Portal { get; } = portal;

        internal Dialog Declaration { get; set; } = portal.Widget;

        internal CancellationTokenSource Cancellation { get; } = new();

        internal UiWindowId? Window { get; set; }

        internal UiSurface? Surface { get; set; }

        internal UiPopupOwner? Owner { get; set; }

        internal UiPopupPortalCoordinator? Portals { get; set; }

        internal UiDialogPortalCoordinator? Dialogs { get; set; }

        internal IDisposable? Modal { get; set; }

        internal IDisposable? LogicalWindow { get; set; }

        internal UiFocusBookmark? Bookmark { get; set; }

        internal UiSurface? PreviousSurface { get; set; }

        internal UiWindowId PreviousWindow { get; set; }

        internal long? KeyboardGeneration { get; set; }

        internal long FocusRequest { get; set; }

        internal Task<IAsyncDisposable>[] Guards { get; set; } = [];

        internal Task[]? GuardRelease { get; set; }

        internal List<IAsyncDisposable> Participants { get; } = [];

        internal Task[]? Cleanup { get; set; }

        internal Task<UiWindowBinding>? BindingTask { get; set; }

        internal UiWindowBinding? Binding { get; set; }

        internal Task? Retirement { get; set; }

        internal UiWindowRequestId? Show { get; set; }

        internal UiWindowRequestId? Close { get; set; }

        internal bool Opened { get; set; }

        internal bool Closing { get; set; }

        internal bool Failed { get; set; }

        internal bool Terminal { get; set; }

        internal bool Notified { get; set; }

        internal bool ResetRequested { get; set; }

        internal string Code { get; set; } = string.Empty;
    }
}
