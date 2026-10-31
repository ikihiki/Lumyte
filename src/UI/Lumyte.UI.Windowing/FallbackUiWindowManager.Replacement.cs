namespace Lumyte.UI.Windowing;

/// <summary>Coordinates staged publication and retained cleanup on the UI owner.</summary>
public sealed partial class FallbackUiWindowManager
{
    /// <inheritdoc/>
    public IUiWindowPresentationReplacement BeginReplacement(UiWindowId window, UiWindowBinding? current, UiSurface candidate)
    {
        Admit();
        Entry entry = Get(window);
        if (entry.Replacing || entry.Closing || entry.FallingBack || entry.Retiring || !ReferenceEquals(current, entry.PublicBinding))
        {
            throw new InvalidOperationException("The current binding is not eligible for replacement.");
        }

        if (entry.Manager is not IUiWindowPresentationReplacementManager manager)
        {
            throw new NotSupportedException("presentation-replacement-unavailable");
        }

        IUiWindowPresentationReplacement inner = manager.BeginReplacement(entry.Inner, entry.Binding, candidate);
        var replacement = new Replacement(this, entry, candidate, inner);
        entry.Replacing = true;
        return replacement;
    }

    private sealed class Replacement(FallbackUiWindowManager manager, Entry entry, UiSurface candidate, IUiWindowPresentationReplacement inner) : IUiWindowPresentationReplacement
    {
        private readonly UiWindowBinding _public = new(entry.Id, () => manager.Retire(entry));
        private readonly TaskCompletionSource _retired = new();
        private UiWindowBinding? _previous = entry.PublicBinding;
        private Func<ValueTask>? _releasePrevious = entry.Binding is { } binding ? binding.DisposeAsync : null;

        public ValueTask PrepareAsync(CancellationToken cancellationToken) => inner.PrepareAsync(cancellationToken);

        public bool TryValidateCommit(out string code)
        {
            code = "candidate-stale";
            return !entry.Closing && !entry.Terminal && !manager._stopping && inner.TryValidateCommit(out code);
        }

        public UiWindowBinding Commit()
        {
            UiWindowBinding binding = inner.Commit();
            _previous?.Redirect(_releasePrevious!);
            entry.Binding = binding;
            entry.PublicBinding = _public;
            entry.Surface = candidate;
            entry.Retired = _retired;
            entry.Replacing = false;
            return _public;
        }

        public ValueTask DrainPreviousAsync(CancellationToken cancellationToken) => inner.DrainPreviousAsync(cancellationToken);

        public async ValueTask ReleasePreviousAsync(CancellationToken cancellationToken)
        {
            await inner.ReleasePreviousAsync(cancellationToken);
            if (_previous is not null)
            {
                await _previous.DisposeAsync();
            }

            _previous = null;
            _releasePrevious = null;
        }

        public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => inner.DrainCandidateAsync(cancellationToken);

        public async ValueTask DiscardAsync(CancellationToken cancellationToken)
        {
            await inner.DiscardAsync(cancellationToken);
            entry.Replacing = false;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
