using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing.Tests;

internal sealed class TestSource : IUiWindowSource
{
    public Guid SourceId { get; } = Guid.NewGuid();

    internal InputWindowId Origin { get; } = new(Guid.NewGuid());

    internal Vector2 Size { get; set; } = new(200, 150);

    internal Vector2 Scale { get; set; } = Vector2.One;

    internal bool Visible { get; set; } = true;

    internal bool Active { get; set; } = true;

    internal bool Acknowledge { get; set; } = true;

    internal TaskCompletionSource<IUiWindowPresentation>? PendingPrepare { get; set; }

    internal TaskCompletionSource? RetireGate { get; set; }

    internal bool FailRetirement { get; set; }

    internal List<Presentation> Presentations { get; } = [];

    internal Guid Scope { get; } = Guid.NewGuid();

    public UiWindowSourceSnapshot CaptureState() => new(1, Origin, Size, Scale, Visible, Active, false, Scope);

    public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var p = new Presentation(this, view);
        Presentations.Add(p);
        return PendingPrepare is { } pending ? new(pending.Task) : ValueTask.FromResult<IUiWindowPresentation>(p);
    }

    public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
    {
        transformed = position;
        return ReferenceEquals(this, destination);
    }

    internal void Present()
    {
        if (Acknowledge)
        {
            foreach (Presentation p in Presentations.Where(p => !p.Disposed))
            {
                p.PresentedGeometryRevision = p.View.GeometryRevision;
            }
        }
    }

    internal sealed class Presentation(TestSource source, UiWindowPresentationView view) : IUiWindowPresentation
    {
        public ulong PresentedGeometryRevision { get; set; }

        internal UiWindowPresentationView View { get; private set; } = view;

        internal bool Disposed { get; private set; }

        public void Update(UiWindowPresentationView next) => View = next;

        public async ValueTask DisposeAsync()
        {
            if (source.RetireGate is { } gate)
            {
                await gate.Task;
            }

            Disposed = true;
            if (source.FailRetirement)
            {
                throw new InvalidOperationException("retired presentation fixture");
            }
        }
    }
}
