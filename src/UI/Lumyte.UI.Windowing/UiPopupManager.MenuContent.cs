namespace Lumyte.UI.Windowing;

/// <summary>Owns popup content, menu input scopes and cleanup above an injected window manager.</summary>
public sealed partial class UiPopupManager
{
    /// <summary>Resolves an application owner at the next manager drain, after initial binding.</summary>
    /// <param name="owner">A borrowed resolver, invoked only on the owner loop.</param>
    /// <param name="request">The independent content request.</param>
    /// <returns>A cancellable reservation created before any resolver callback.</returns>
    internal IDisposable QueueOpenAt(Func<UiPopupOwner> owner, UiPopupRequest request)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        var reservation = new QueuedPopupRequest();
        UiSurface? admittedSurface = request.Anchor.Surface;
        _commands.Enqueue(() =>
        {
            UiSurface? prepared = null;
            string failure = "owner-unavailable";
            try
            {
                if (!reservation.Cancelled)
                {
                    UiPopupOwner resolved = ResolveDialogOwner(owner(), admittedSurface);
                    if (!reservation.Cancelled)
                    {
                        if (!QueuedAnchorAvailable(resolved, request.Anchor, admittedSurface))
                        {
                            _callbacks.Enqueue(() => request.OnClosed?.Invoke(new(Guid.Empty, UiPopupCloseReason.AnchorUnavailable, "anchor-unavailable")));
                            return;
                        }

                        failure = "invalid-content";
                        Lumyte.UI.Compose.Definitions.Widget content = UiSurface.CaptureBuild(request.CreateContent);
                        prepared = new(content, independentContent: true);
                        if (!reservation.Cancelled && !QueuedAnchorAvailable(resolved, request.Anchor, admittedSurface))
                        {
                            _callbacks.Enqueue(() => request.OnClosed?.Invoke(new(Guid.Empty, UiPopupCloseReason.AnchorUnavailable, "anchor-unavailable")));
                            return;
                        }

                        if (!reservation.Cancelled)
                        {
                            request.PreparedSurface = prepared;
                            request.PreparedContent = content;
                            failure = "owner-unavailable";
                            reservation.Handle = RequestOpenAt(resolved, request);
                            prepared = null;
                            return;
                        }
                    }
                }

                _callbacks.Enqueue(() => request.OnClosed?.Invoke(new(Guid.Empty, UiPopupCloseReason.Requested, string.Empty)));
            }
            catch (Exception)
            {
                _callbacks.Enqueue(() => request.OnClosed?.Invoke(new(Guid.Empty, UiPopupCloseReason.OpenFailed, failure)));
            }
            finally
            {
                prepared?.Dispose();
            }
        });
        return reservation;
    }

    /// <summary>Queues a request against its anchor's actual owner or open menu surface.</summary>
    /// <param name="owner">The exact borrowed root owner.</param>
    /// <param name="request">The anchor identifies the logical parent, never its position in an index.</param>
    /// <returns>A queued request; no content callback runs during admission.</returns>
    internal UiPopupHandle RequestOpenAt(UiPopupOwner owner, UiPopupRequest request)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);
        if (!_owners.TryGetValue(owner.Id, out Owner? current) || !ReferenceEquals(current.Token, owner))
        {
            throw new ArgumentException("Unknown owner.", nameof(owner));
        }

        if (ReferenceEquals(request.Anchor.Surface, current.Surface))
        {
            return RequestOpen(owner, request);
        }

        Popup? parent = _popups.Values.SingleOrDefault(p => p.Owner == current && ReferenceEquals(p.Surface, request.Anchor.Surface));
        return parent is not null ? RequestOpen(parent.Handle, request)
            : throw new ArgumentException("Anchor is not on this owner's actual registered surface.", nameof(request));
    }

    /// <summary>Queues command dismissal for exactly the menu chain containing this live element.</summary>
    /// <param name="anchor">The actual command element, not an automation role inference.</param>
    internal void RequestItemInvoked(UiElement anchor)
    {
        Admit();
        ArgumentNullException.ThrowIfNull(anchor);
        Popup? popup = _popups.Values.SingleOrDefault(p => ReferenceEquals(p.Surface, anchor.Surface)
            && p.Mode == UiPopupInputMode.Menu && p.Handle.State == UiPopupState.Open && !p.Closing);
        if (popup is null)
        {
            return;
        }

        while (popup.Parent is not null)
        {
            popup = popup.Parent;
        }

        Popup root = popup;
        _commands.Enqueue(() =>
        {
            if (!root.Closing && _popups.TryGetValue(root.Handle.Id, out Popup? current) && ReferenceEquals(current, root))
            {
                if (root.Request?.OnDismissRequested is { } notify)
                {
                    _callbacks.Enqueue(() =>
                    {
                        if (!root.Closing && root.Request is not null)
                        {
                            notify(new(PopupDismissReason.ItemInvoked));
                        }
                    });
                }
                else
                {
                    Close(root, UiPopupCloseReason.Requested);
                }
            }
        });
    }

    private bool QueuedAnchorAvailable(UiPopupOwner owner, UiElement anchor, UiSurface? admittedSurface)
    {
        if (admittedSurface is null || !ReferenceEquals(anchor.Surface, admittedSurface)
            || !_owners.TryGetValue(owner.Id, out Owner? current) || !ReferenceEquals(current.Token, owner)
            || current.Releasing || current.Paused || owner.IsReleaseRequested
            || !AllowsModal(FindWindow(admittedSurface) ?? current.Window)
            || !ReferenceEquals(anchor.InputVersion, admittedSurface.InputVersionForWindowing)
            || anchor.InputBounds.Right <= anchor.InputBounds.Left || anchor.InputBounds.Bottom <= anchor.InputBounds.Top)
        {
            return false;
        }

        if (!ReferenceEquals(current.Surface, admittedSurface) && !_popups.Values.Any(p => p.Owner == current
            && ReferenceEquals(p.Surface, admittedSurface) && p.Handle.State == UiPopupState.Open && !p.Closing))
        {
            return false;
        }

        for (UiElement? element = anchor; element is not null; element = element.Parent)
        {
            if (!UiInputGeometry.AcceptsInput(element))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class QueuedPopupRequest : IDisposable
    {
        internal bool Cancelled { get; private set; }

        internal UiPopupHandle? Handle { get; set; }

        public void Dispose()
        {
            Cancelled = true;
            Handle?.Close();
        }
    }
}
