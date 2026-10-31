using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

/// <summary>Coordinates staged publication and retained cleanup on the UI owner.</summary>
public sealed partial class UiApplication
{
    internal Action PrepareInputReplacement(UiApplicationViewSlot slot, UiSurface? previous, UiSurface candidate)
    {
        Entry entry = _entries.Values.Single(entry => ReferenceEquals(entry.Slot, slot));
        return _router?.PrepareReplacement(previous, candidate, entry.Root.NativeWindow!, () => entry.InputAttached = true) ?? NoInputCommit;
    }

    internal void QuiesceInput(UiSurface surface) => _router?.CancelSurface(surface);

    internal void ForgetRetiredSurface(UiSurface surface)
    {
        _inactiveFocus.Remove(surface);
        _recoveryFocus.Remove(surface);
    }

    private static void NoInputCommit()
    {
    }

    private void ConfigureSource(UiDriverWindow window)
    {
        window.Source.ValidateRoot = ValidateEditors;
        window.Source.PrepareCandidate = (DrawList2D commands, CancellationToken cancellationToken) => Driver.PrepareCandidateAsync(window, commands, cancellationToken);
    }
}
