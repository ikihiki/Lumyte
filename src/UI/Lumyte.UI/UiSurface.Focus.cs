namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    /// <summary>Captures focus, including the absence of focus, without operating-system activation.</summary>
    /// <returns>An opaque attachment-specific bookmark.</returns>
    public UiFocusBookmark CaptureFocus()
    {
        ThrowIfDisposed();
        return new(this, _focusedElement);
    }

    /// <summary>Restores focus only if the bookmarked attachment remains eligible.</summary>
    /// <param name="bookmark">A bookmark from this surface.</param>
    /// <returns>Whether focus was restored; stale bookmarks leave focus unchanged.</returns>
    public bool TryRestoreFocus(UiFocusBookmark bookmark)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(bookmark);
        if (!ReferenceEquals(bookmark.Surface, this))
        {
            throw new ArgumentException("The bookmark belongs to another surface.", nameof(bookmark));
        }

        UiElement? target = bookmark.Target;
        if (target is not null && (!ReferenceEquals(target.Surface, this) || target.AutomationId != bookmark.Identity || !IsFocusEligible(target)))
        {
            return false;
        }

        CancelAllInput();
        return ChangeFocus(target);
    }

    /// <summary>Moves focus in the existing eligible candidate order without synthesizing keys.</summary>
    /// <param name="navigation">The candidate direction.</param>
    /// <returns>Whether a candidate exists.</returns>
    public bool MoveFocus(UiFocusNavigation navigation)
    {
        ThrowIfDisposed();
        if (_focusCandidates.Count == 0)
        {
            return false;
        }

        int current = _focusCandidates.IndexOf(_focusedElement!);
        int next = navigation switch
        {
            UiFocusNavigation.First => 0,
            UiFocusNavigation.Last => _focusCandidates.Count - 1,
            UiFocusNavigation.Next => (current + 1) % _focusCandidates.Count,
            UiFocusNavigation.Previous => current <= 0 ? _focusCandidates.Count - 1 : current - 1,
            _ => throw new ArgumentOutOfRangeException(nameof(navigation)),
        };
        return ChangeFocus(_focusCandidates[next]);
    }
}
