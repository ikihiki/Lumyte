using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>Routes modal normal windows independently from Menu popup input modes.</summary>
public sealed partial class UiPopupManager
{
    private readonly List<ModalScope> _modalScopes = [];
    private readonly Dictionary<UiWindowId, UiWindowId> _logicalOwners = [];
    private readonly Dictionary<UiWindowId, Action<UiDialogCloseRequestReason>> _dialogCloseRequests = [];

    internal void ValidateDialogAdmission()
    {
        Admit();
        if (_dialogCloseRequests.Count >= 16)
        {
            throw new InvalidOperationException("The manager already owns sixteen dialog sessions.");
        }
    }

    internal IDisposable RegisterLogicalChild(UiWindowId window, UiWindowId owner, Action<UiDialogCloseRequestReason>? closeRequest = null)
    {
        _logicalOwners.Add(window, owner);
        if (closeRequest is not null)
        {
            _dialogCloseRequests.Add(window, closeRequest);
        }

        return new InputRegistration(() =>
        {
            _logicalOwners.Remove(window);
            _dialogCloseRequests.Remove(window);
        });
    }

    internal IDisposable EnterModal(UiWindowId window, UiWindowId ownerWindow, Action<bool> requestClose)
    {
        Check();
        if (!_windows.TryGetSnapshot(ownerWindow, out UiWindowSnapshot? state))
        {
            throw new InvalidOperationException("The modal window must already be reserved.");
        }

        var scope = new ModalScope(this, window, ownerWindow, state!.KeyboardScopeId, requestClose);
        _modalScopes.Add(scope);
        try
        {
            foreach (Popup popup in _popups.Values.Where(popup => WithinLogical(popup.Owner.Window, ownerWindow)).ToArray())
            {
                popup.SuppressRestore = true;
                Close(popup, UiPopupCloseReason.OwnerUnavailable);
            }

            foreach ((UiWindowId candidateWindow, UiSurface candidateSurface) in _candidateSurfaces.ToArray())
            {
                if (WithinLogical(candidateWindow, ownerWindow) && !Within(candidateWindow, window))
                {
                    CancelWindow(candidateWindow, candidateSurface);
                }
            }

            foreach (Owner owner in _owners.Values.Where(owner => WithinLogical(owner.Window, ownerWindow) && !Within(owner.Window, window)).ToArray())
            {
                CancelWindow(owner.Window, owner.Surface);
            }
        }
        catch
        {
            scope.Dispose();
            throw;
        }

        return scope;
    }

    internal (UiSurface Surface, UiWindowId Window, UiFocusBookmark Bookmark, long Request) CaptureDialogReturn(UiWindowId ownerWindow, UiSurface ownerSurface)
    {
        Popup? menu = _popups.Values.Where(popup => popup.Accepted && popup.Mode == UiPopupInputMode.Menu && WithinLogical(popup.Owner.Window, ownerWindow)).OrderByDescending(popup => popup.Depth).FirstOrDefault();
        if (menu is not null)
        {
            while (menu.Parent is not null)
            {
                menu = menu.Parent;
            }

            if (menu.Previous is { } previous && menu.Bookmark is { } bookmark)
            {
                return (previous, menu.Owner.Window, bookmark, menu.PreviousFocusRequest);
            }
        }

        UiWindowId target = _windows.GetKeyboardTarget(ownerWindow) ?? ownerWindow;
        UiSurface surface = WithinLogical(target, ownerWindow) ? Surface(target) ?? ownerSurface : ownerSurface;
        UiWindowId window = FindWindow(surface) ?? ownerWindow;
        return (surface, window, surface.CaptureFocus(), surface.FocusRequest);
    }

    internal Task<IAsyncDisposable>[] PrepareDialogGuards(UiPopupOwner owner, CancellationToken cancellation) =>
        _menuGuards.GetValueOrDefault(owner.Id, []).ToArray().Select(prepare => PrepareGuard(prepare, cancellation)).ToArray();

    internal bool HasClosingMenus(UiWindowId window) => _windows.TryGetSnapshot(window, out UiWindowSnapshot? state)
        && _popups.Values.Any(popup => popup.Closing && SameScopeRoot(popup.Owner, state!));

    internal UiWindowId? CurrentModalTarget(UiWindowId owner) => _windows.TryGetSnapshot(owner, out UiWindowSnapshot? state)
        ? CurrentModal(state!.KeyboardScopeId, owner)?.Window : null;

    internal UiSurface? BorrowRegisteredSurface(UiWindowId window) => Surface(window);

    internal bool AllowsModal(UiWindowId window)
    {
        if (_modalScopes.Count == 0)
        {
            return true;
        }

        if (!_windows.TryGetSnapshot(window, out UiWindowSnapshot? state))
        {
            return false;
        }

        ModalScope? modal = CurrentModal(state!.KeyboardScopeId, window);
        return modal is null || Within(window, modal.Window);
    }

    private bool ModalOwns(UiWindowId window) => _modalScopes.Count != 0 && _windows.TryGetSnapshot(window, out UiWindowSnapshot? state)
        && CurrentModal(state!.KeyboardScopeId, window) is not null;

    private UiPopupOwner ResolveDialogOwner(UiPopupOwner root, UiSurface? surface)
    {
        if (_dialogCloseRequests.Count == 0)
        {
            return root;
        }

        if (!_owners.TryGetValue(root.Id, out Owner? ancestor) || ancestor.Token != root || ancestor.Releasing || root.IsReleaseRequested)
        {
            throw new InvalidOperationException("The borrowed owner is no longer live.");
        }

        Owner? actual = _owners.Values.FirstOrDefault(owner => ReferenceEquals(owner.Surface, surface))
            ?? _popups.Values.FirstOrDefault(popup => ReferenceEquals(popup.Surface, surface))?.Owner;
        return actual is not null && WithinLogical(actual.Window, ancestor.Window) ? actual.Token : root;
    }

    private ModalScope? CurrentModal(Guid scope, UiWindowId window)
    {
        ModalScope? applicable = _modalScopes.LastOrDefault(modal => modal.Scope == scope && WithinLogical(window, modal.Owner));
        return applicable is null ? null : _modalScopes.LastOrDefault(modal => modal.Scope == scope && WithinLogical(modal.Owner, applicable.Owner)) ?? applicable;
    }

    private bool WithinLogical(UiWindowId window, UiWindowId owner)
    {
        for (int depth = 0; depth < 64; depth++)
        {
            if (window == owner)
            {
                return true;
            }

            if (_logicalOwners.TryGetValue(window, out UiWindowId dialogOwner))
            {
                window = dialogOwner;
                continue;
            }

            if (!_windows.TryGetSnapshot(window, out UiWindowSnapshot? state) || state!.Role is UiWindowRole.Normal or UiWindowRole.Tool || state.Owner is not { } parent)
            {
                return false;
            }

            window = parent;
        }

        return false;
    }

    private bool Within(UiWindowId window, UiWindowId ancestor)
    {
        for (int depth = 0; depth < 64; depth++)
        {
            if (window == ancestor)
            {
                return true;
            }

            if (!_windows.TryGetSnapshot(window, out UiWindowSnapshot? state) || state!.Owner is not { } parent)
            {
                return false;
            }

            window = parent;
        }

        return false;
    }

    private bool FilterModal(UiWindowInputEnvelope envelope, UiWindowId? root)
    {
        if (_modalScopes.Count == 0 || root is not { } origin || !_windows.TryGetSnapshot(origin, out UiWindowSnapshot? state))
        {
            return false;
        }

        UiInput input = envelope.Input;
        if ((input is UiPointerInput heldPointer && _pointers.ContainsKey(heldPointer.PointerId))
            || (input is UiKeyInput heldKey && _keys.ContainsKey((envelope.Device, heldKey.Key))))
        {
            return false;
        }

        Vector2? position = input switch
        {
            UiPointerInput pointer => pointer.Position,
            UiWheelInput wheel => wheel.Position,
            UiCheckInput check => check.Position,
            UiExpandInput expand => expand.Position,
            UiSetNumericInput numeric => numeric.Position,
            _ => null,
        };
        UiWindowHit? hit = position is { } point ? _windows.HitTest(origin, point) : null;
        UiWindowId destination = hit?.Window ?? _windows.GetKeyboardTarget(origin) ?? origin;
        ModalScope? modal = CurrentModal(state!.KeyboardScopeId, destination);
        if (modal is null)
        {
            return false;
        }

        if (Within(destination, modal.Window) && Surface(destination) is null)
        {
            if (input is UiPointerInput { Phase: UiPointerPhase.Down } closingPointer)
            {
                _pointers[closingPointer.PointerId] = null;
            }
            else if (input is UiKeyInput { IsDown: true } closingKey)
            {
                _keys[(envelope.Device, closingKey.Key)] = null;
            }

            return true;
        }

        if (position is not null)
        {
            if (hit is { } unrelated && CurrentModal(state!.KeyboardScopeId, unrelated.Window) is null)
            {
                return false;
            }

            if (hit is null || !Within(hit.Value.Window, modal.Window))
            {
                if (input is UiPointerInput { Phase: UiPointerPhase.Down } outside)
                {
                    _pointers[outside.PointerId] = null;
                    modal.RequestClose(false);
                }

                return true;
            }
        }
        else if (_windows.GetKeyboardTarget(origin) is { } unrelatedTarget && CurrentModal(state!.KeyboardScopeId, unrelatedTarget) is null)
        {
            return false;
        }
        else if (input is UiKeyInput { Key: Key.Escape, IsDown: true, IsRepeat: false } escape
            && Deepest(state!.KeyboardScopeId) is null)
        {
            _keys[(envelope.Device, escape.Key)] = null;
            modal.RequestClose(true);
            return true;
        }
        else if (input is UiKeyInput && (_windows.GetKeyboardTarget(origin) is not { } target || !Within(target, modal.Window))
            && Deepest(state!.KeyboardScopeId) is null)
        {
            if (input is UiKeyInput { IsDown: true } blockedKey)
            {
                _keys[(envelope.Device, blockedKey.Key)] = null;
            }

            return true;
        }

        return false;
    }

    private sealed class ModalScope(UiPopupManager manager, UiWindowId window, UiWindowId owner, Guid scope, Action<bool> requestClose) : IDisposable
    {
        internal UiWindowId Window { get; } = window;

        internal UiWindowId Owner { get; } = owner;

        internal Guid Scope { get; } = scope;

        internal Action<bool> RequestClose { get; } = requestClose;

        public void Dispose() => manager._modalScopes.Remove(this);
    }
}
