using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.SampleSupport;

/// <summary>Sample-only compositor. The host serializes frames and calls CompleteFrame only after GPU completion/retirement.</summary>
internal sealed class UiHostedDrawingSource(Func<UiWindowSourceSnapshot> capture) : IUiWindowSource
{
    private readonly List<Presentation> _presentations = [];

    private readonly Dictionary<Presentation, UiWindowPresentationView> _recorded = [];

    private TaskCompletionSource? _frame;

    public Guid SourceId { get; } = Guid.NewGuid();

    public UiWindowSourceSnapshot CaptureState() => capture();

    public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var presentation = new Presentation(this, surface, view);
        _presentations.Add(presentation);
        return ValueTask.FromResult<IUiWindowPresentation>(presentation);
    }

    public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
    {
        transformed = position;
        return ReferenceEquals(destination, this);
    }

    internal void Record(DrawingCanvas2D canvas)
    {
        if (_frame is not null)
        {
            throw new InvalidOperationException("Finish the previous frame before recording another.");
        }

        _frame = new();
        _recorded.Clear();
        foreach (Presentation presentation in _presentations.Where(p => !p.Closing).OrderBy(p => p.View.Role == UiWindowRole.ImeCandidates ? 2 : p.View.Role is UiWindowRole.Popup or UiWindowRole.ContextMenu ? 1 : 0).ThenBy(p => p.View.StackingOrder == 0 ? p.View.Window.Value : p.View.StackingOrder).ToArray())
        {
            UiWindowPresentationView view = presentation.View;
            _recorded.Add(presentation, view);
            if (!view.IsVisible)
            {
                continue;
            }

            canvas.Save();
            try
            {
                canvas.Scale(view.Scale);
                canvas.ClipRect(view.ClipInPresentation);
                canvas.Translate(new(view.BoundsInPresentation.X, view.BoundsInPresentation.Y));
                presentation.Surface.Record(canvas);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }

    internal void CompleteFrame(bool presented = true)
    {
        if (_frame is not { } frame)
        {
            return;
        }

        if (presented)
        {
            foreach ((Presentation token, UiWindowPresentationView view) in _recorded)
            {
                token.Presented = view;
                token.PresentedGeometryRevision = view.GeometryRevision;
            }
        }

        _frame = null;
        _recorded.Clear();
        frame.TrySetResult();
    }

    internal UiWindowPresentationView Presented(UiWindowId window) => _presentations.Single(p => p.View.Window == window).Presented ?? throw new InvalidOperationException("No frame has been presented.");

    private sealed class Presentation(UiHostedDrawingSource source, UiSurface surface, UiWindowPresentationView view) : IUiWindowPresentation
    {
        private Task? _retirement;

        public ulong PresentedGeometryRevision { get; set; }

        internal UiSurface Surface { get; } = surface;

        internal UiWindowPresentationView View { get; private set; } = view;

        internal UiWindowPresentationView? Presented { get; set; }

        internal bool Closing { get; private set; }

        public void Update(UiWindowPresentationView next) => View = next;

        public ValueTask DisposeAsync()
        {
            Closing = true;
            _retirement ??= RetireAsync();
            return new(_retirement);
        }

        private async Task RetireAsync()
        {
            if (source._frame is { } frame)
            {
                await frame.Task;
            }

            source._presentations.Remove(this);
        }
    }
}
