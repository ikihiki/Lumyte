using System.Numerics;

namespace Lumyte.UI.Windowing;

internal sealed class UiWindowSourceProjection(Guid owner, IUiWindowSource source, Func<UiWindowPresentationView, UiWindowPresentationView> project, Action<UiWindowId, ulong> presented) : IUiWindowSourceAdapter
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
}
