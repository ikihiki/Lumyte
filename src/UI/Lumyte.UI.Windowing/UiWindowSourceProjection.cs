using System.Numerics;

namespace Lumyte.UI.Windowing;

internal sealed class UiWindowSourceProjection(Guid owner, IUiWindowSource source, Func<UiWindowPresentationView, UiWindowPresentationView> project, Action<UiWindowId, ulong> presented) : IUiWindowSourceAdapter, IUiWindowPresentationStager
{
    public Guid SourceId => source.SourceId;

    public IUiWindowSource InnerSource => source;

    internal Guid Owner { get; } = owner;

    public UiWindowSourceSnapshot CaptureState() => source.CaptureState();

    public IUiWindowSource AdaptSource(IUiWindowSource next) => new UiWindowSourceProjection(Owner, source is IUiWindowSourceAdapter adapter ? adapter.AdaptSource(next) : next, project, presented);

    public async ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken)
    {
        UiWindowPresentationView projected = project(view);
        IUiWindowPresentation token = await source.PreparePresentationAsync(projected, surface, cancellationToken);
        return new Presentation(token, view, projected, project, presented);
    }

    public IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate)
    {
        if (source is not IUiWindowPresentationStager stager)
        {
            throw new NotSupportedException("presentation-replacement-unavailable");
        }

        UiWindowPresentationView projected = project(view);
        return new Staged(stager.BeginReplacement(current is Presentation token ? token.Inner : current is null ? null : throw new ArgumentException("Foreign current presentation.", nameof(current)), projected, candidate), view, projected, project, presented);
    }

    public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed) => source.TryTransformPoint(Unwrap(destination), position, out transformed);

    internal static IUiWindowSource Unwrap(IUiWindowSource source)
    {
        while (source is IUiWindowSourceAdapter adapter)
        {
            source = adapter.InnerSource;
        }

        return source;
    }

    private sealed class Presentation(IUiWindowPresentation token, UiWindowPresentationView original, UiWindowPresentationView projected, Func<UiWindowPresentationView, UiWindowPresentationView> project, Action<UiWindowId, ulong> presented) : IUiWindowPresentation
    {
        private UiWindowPresentationView _original = original;
        private UiWindowPresentationView _projected = projected;
        private ulong _acknowledged;
        private ulong _publicAcknowledged;

        public ulong PresentedGeometryRevision
        {
            get
            {
                if (token.PresentedGeometryRevision >= _projected.GeometryRevision && _publicAcknowledged != _projected.GeometryRevision)
                {
                    _acknowledged = _original.GeometryRevision;
                    _publicAcknowledged = _projected.GeometryRevision;
                    presented(_projected.Window, _publicAcknowledged);
                }

                return _acknowledged;
            }
        }

        internal IUiWindowPresentation Inner => token is Staged.Deferred deferred ? deferred.Inner : token;

        public void Update(UiWindowPresentationView view)
        {
            if (view != _original)
            {
                _original = view;
                _projected = project(view);
            }

            token.Update(_projected);
        }

        public ValueTask DisposeAsync() => token.DisposeAsync();
    }

    private sealed class Staged(IUiStagedWindowPresentation inner, UiWindowPresentationView original, UiWindowPresentationView projected, Func<UiWindowPresentationView, UiWindowPresentationView> project, Action<UiWindowId, ulong> presented) : IUiStagedWindowPresentation
    {
        private IUiWindowPresentation? _prepared;
        private Presentation? _publication;

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            await inner.PrepareAsync(cancellationToken);

            // The wrapper is allocated before Commit; the inner drawing registration remains private.
            _publication = new(new Deferred(this), original, projected, project, presented);
        }

        public bool TryValidateCommit(out string code) => inner.TryValidateCommit(out code);

        public IUiWindowPresentation Commit()
        {
            _prepared = inner.Commit();
            return _publication!;
        }

        public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => inner.DrainCandidateAsync(cancellationToken);

        public ValueTask DiscardAsync(CancellationToken cancellationToken) => inner.DiscardAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        internal sealed class Deferred(Staged owner) : IUiWindowPresentation
        {
            public ulong PresentedGeometryRevision => owner._prepared?.PresentedGeometryRevision ?? 0;

            internal IUiWindowPresentation Inner => owner._prepared!;

            public void Update(UiWindowPresentationView view) => owner._prepared!.Update(view);

            public ValueTask DisposeAsync() => owner._prepared!.DisposeAsync();
        }
    }
}
