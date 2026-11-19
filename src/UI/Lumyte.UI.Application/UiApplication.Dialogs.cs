using Lumyte.UI.Automation;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Owns declarative dialog progression on the standard application loop.</summary>
public sealed partial class UiApplication
{
    private readonly HashSet<UiDialogPortalCoordinator> _dialogCoordinators = [];
    private readonly Dictionary<Guid, PopupSite> _dialogHosts = [];

    internal UiDialogPortalCoordinator AttachDialogs(UiWindowId window, UiPopupOwner owner, UiSurface surface, bool staged)
    {
        var dialogs = new UiDialogPortalCoordinator(Windows, Popups, owner, window, surface, staged);
        _dialogCoordinators.Add(dialogs);
        return dialogs;
    }

    internal IReadOnlyList<UiDialogOpened> CaptureDialogs() => _dialogCoordinators.SelectMany(dialogs => dialogs.CaptureDialogs()).ToArray();

    internal IUiAutomationHost DialogAutomation(Guid id, bool inspection = false) => _dialogHosts.TryGetValue(id, out PopupSite? site)
        ? site.GetAutomation(inspection) : throw new InvalidOperationException("dialog-unavailable");

    internal bool TryGetDialogSurface(Guid id, out UiSurface? surface)
    {
        foreach (UiDialogPortalCoordinator dialogs in _dialogCoordinators)
        {
            if (dialogs.TryGetSurface(id, out surface))
            {
                return true;
            }
        }

        surface = null;
        return false;
    }

    internal void BindDialogLifetime(Guid id, IAsyncDisposable participant)
    {
        UiDialogPortalCoordinator coordinator = _dialogCoordinators.Single(dialogs => dialogs.CaptureDialogs().Any(dialog => dialog.DialogId == id));
        coordinator.BindLifetime(id, participant);
    }

    private void AdvanceDialogs(bool animationFrame)
    {
        foreach (UiDialogPortalCoordinator dialogs in _dialogCoordinators.ToArray())
        {
            dialogs.Advance();
            if (dialogs.IsStopped)
            {
                _dialogCoordinators.Remove(dialogs);
                continue;
            }

            foreach (UiDialogOpened dialog in dialogs.CaptureDialogs())
            {
                if (!dialogs.TryGetSurface(dialog.DialogId, out UiSurface? surface) || surface is null)
                {
                    continue;
                }

                ValidateEditors(surface);
                if (!_dialogHosts.ContainsKey(dialog.DialogId))
                {
                    var host = UiApplicationAutomationHost.Create(this, () => surface);
                    TextSite? text = AttachText(surface, dialog.Window, host);
                    IDisposable? guard = text is null ? null : dialogs.RegisterTextGuard(dialog.DialogId, text.Connection.SuspendAsync);
                    var site = new PopupSite(this, dialog.DialogId, surface, host, text, guard);
                    _dialogHosts.Add(dialog.DialogId, site);
                    dialogs.BindLifetime(dialog.DialogId, site);
                    if (Popups.VisualLifetime?.Invoke(surface) is { } visual)
                    {
                        dialogs.BindLifetime(dialog.DialogId, visual);
                    }
                }

                surface.SetAnimationClock(AnimationClock);
                surface.SetAnimationUpdatesEnabled(State == UiApplicationState.Running && Driver.CanAnimate);
                if (animationFrame || surface.HasPendingAnimationUpdates)
                {
                    surface.AdvanceAnimations();
                }

                surface.UpdateLayout();
                (surface.VisualHost as UiVisualCoordinator)?.Advance();
            }
        }
    }
}
