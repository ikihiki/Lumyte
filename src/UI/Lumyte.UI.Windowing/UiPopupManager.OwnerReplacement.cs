namespace Lumyte.UI.Windowing;

/// <summary>Coordinates staged publication and retained cleanup on the UI owner.</summary>
public sealed partial class UiPopupManager
{
    internal PreparedOwner PrepareOwnerReplacement(UiPopupOwner? current, UiWindowId window, UiSurface surface, Func<CancellationToken, ValueTask<IAsyncDisposable>>? menuGuard)
    {
        Admit();
        Owner? old = current is null ? null : _owners.GetValueOrDefault(current.Id);
        if (current is not null && (old is null || old.Releasing))
        {
            throw new InvalidOperationException("The previous popup owner is not live.");
        }

        UiPopupOwner? token = null;
        token = new(() => _commands.Enqueue(() => ReleaseOwner(token!.Id)));
        var candidate = new Owner(token, window, surface);
        _owners.EnsureCapacity(_owners.Count + 1);
        var guards = new List<Func<CancellationToken, ValueTask<IAsyncDisposable>>>();
        if (menuGuard is not null)
        {
            guards.Add(menuGuard);
        }

        _menuGuards.Add(token.Id, guards);
        return new(this, old, candidate);
    }

    internal void GateOwner(UiPopupOwner owner, bool gated)
    {
        if (_owners.TryGetValue(owner.Id, out Owner? current))
        {
            current.Paused = gated;
            if (gated)
            {
                CancelWindow(current.Window, current.Surface);
                foreach (Popup popup in _popups.Values.Where(p => p.Owner == current).ToArray())
                {
                    Close(popup, UiPopupCloseReason.Replaced);
                }
            }
        }
    }

    internal bool IsOwnerQuiescent(UiPopupOwner owner) => !_popups.Values.Any(popup => popup.Owner.Token == owner);

    internal sealed class Owner(UiPopupOwner token, UiWindowId window, UiSurface surface)
    {
        internal UiPopupOwner Token { get; } = token;

        internal UiWindowId Window { get; } = window;

        internal UiSurface Surface { get; } = surface;

        internal bool Paused { get; set; }

        internal bool Releasing { get; set; }
    }

    internal sealed class PreparedOwner
    {
        private readonly UiPopupManager _manager;
        private readonly Owner _candidate;
        private Owner? _previous;
        private bool _committed;

        internal PreparedOwner(UiPopupManager manager, Owner? previous, Owner candidate)
        {
            _manager = manager;
            _previous = previous;
            _candidate = candidate;
        }

        internal UiPopupOwner Token => _candidate.Token;

        internal void Commit()
        {
            if (_previous is not null)
            {
                _manager._owners.Remove(_previous.Token.Id);
                _manager._menuGuards.Remove(_previous.Token.Id);
                _previous.Token.IsReleased = true;
                _previous = null;
            }

            _manager._owners.Add(_candidate.Token.Id, _candidate);
            _committed = true;
        }

        internal void Discard()
        {
            if (!_committed)
            {
                _manager._menuGuards.Remove(_candidate.Token.Id);
                _candidate.Token.IsReleased = true;
            }
        }
    }
}
