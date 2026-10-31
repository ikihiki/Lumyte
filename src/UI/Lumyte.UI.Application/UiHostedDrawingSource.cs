using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Standard application compositor. The host serializes frames and calls CompleteFrame only after GPU completion/retirement.</summary>
internal sealed class UiHostedDrawingSource(Func<UiWindowSourceSnapshot> capture) : IUiWindowSource, IUiWindowPresentationStager
{
    private readonly List<Presentation> _presentations = [];

    private readonly Dictionary<Presentation, UiWindowPresentationView> _recorded = [];

    private readonly HashSet<UiSurface> _paused = [];

    private TaskCompletionSource? _frame;

    public Guid SourceId { get; } = Guid.NewGuid();

    internal Action<Lumyte.UI.Compose.Definitions.Widget>? ValidateRoot { get; set; }

    internal Func<DrawList2D, CancellationToken, ValueTask>? PrepareCandidate { get; set; }

    public UiWindowSourceSnapshot CaptureState() => capture();

    public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateRoot?.Invoke(surface.Root);
        var presentation = new Presentation(this, surface, view);
        _presentations.Add(presentation);
        return ValueTask.FromResult<IUiWindowPresentation>(presentation);
    }

    public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
    {
        transformed = position;
        return ReferenceEquals(destination, this);
    }

    public IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate)
    {
        if (current is not null && (current is not Presentation presentation || !ReferenceEquals(presentation.Source, this)))
        {
            throw new ArgumentException("The current presentation belongs to another source.", nameof(current));
        }

        return new Staged(this, current as Presentation, candidate, view);
    }

    internal void Pause(UiSurface surface) => _paused.Add(surface);

    internal void Record(DrawingCanvas2D canvas)
    {
        if (_frame is not null)
        {
            throw new InvalidOperationException("Finish the previous frame before recording another.");
        }

        _frame = new();
        _recorded.Clear();
        try
        {
            foreach (Presentation presentation in _presentations.Where(p => !p.Closing && p.Active).OrderBy(p => UiWindowStacking.Key(p.View.Role, p.View.Window, p.View.StackingOrder)).ToArray())
            {
                UiWindowPresentationView view = _paused.Contains(presentation.Surface) ? presentation.View with { IsVisible = false } : presentation.View;
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
                    ValidateRoot?.Invoke(presentation.Surface.Root);
                    presentation.Surface.Record(canvas);
                }
                finally
                {
                    canvas.Restore();
                }
            }
        }
        catch
        {
            CompleteFrame(false);
            throw;
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

    internal UiWindowPresentationView? Recorded(UiSurface surface) => _recorded.FirstOrDefault(pair => ReferenceEquals(pair.Key.Surface, surface)).Value;

    internal UiWindowPresentationView Presented(UiWindowId window) => _presentations.Single(p => p.View.Window == window).Presented ?? throw new InvalidOperationException("No frame has been presented.");

    private sealed class Presentation(UiHostedDrawingSource source, UiSurface surface, UiWindowPresentationView view) : IUiWindowPresentation
    {
        private Task? _retirement;

        public ulong PresentedGeometryRevision { get; set; }

        internal UiHostedDrawingSource Source => source;

        internal bool Active { get; set; } = true;

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
            source._paused.Remove(Surface);
        }
    }

    private sealed class Staged(UiHostedDrawingSource source, Presentation? current, UiSurface surface, UiWindowPresentationView view) : IUiStagedWindowPresentation
    {
        private Presentation? _previous = current;
        private Presentation? _candidate;
        private object? _inputVersion;
        private bool _committed;
        private bool _discarded;
        private Lumyte.UI.Compose.Definitions.Widget? _root;
        private UiWindowSourceSnapshot? _state;

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.ValidateRoot?.Invoke(surface.Root);
            surface.UpdateLayout();
            _root = surface.Root;
            _inputVersion = surface.InputVersionForWindowing;
            _state = source.CaptureState();
            _candidate = new(source, surface, view) { Active = false };
            source._presentations.Add(_candidate);
            var canvas = new Canvas2D();
            canvas.Begin();
            surface.Record(canvas);
            DrawList2D commands = canvas.End();
            if (source.PrepareCandidate is { } prepare)
            {
                await prepare(commands, cancellationToken);
            }
        }

        public bool TryValidateCommit(out string code)
        {
            code = "candidate-stale";
            return _candidate is not null && !_committed && !_discarded && ReferenceEquals(surface.Root, _root) && ReferenceEquals(surface.InputVersionForWindowing, _inputVersion) && source.CaptureState() == _state;
        }

        public IUiWindowPresentation Commit()
        {
            if (_previous is not null)
            {
                _previous.Active = false;
                _previous = null;
            }

            _candidate!.Active = true;
            _committed = true;
            return _candidate;
        }

        public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public async ValueTask DiscardAsync(CancellationToken cancellationToken)
        {
            if (_candidate is not null)
            {
                await _candidate.DisposeAsync().AsTask().WaitAsync(cancellationToken);
            }

            _discarded = true;
        }

        public ValueTask DisposeAsync()
        {
            if (!_committed && !_discarded)
            {
                throw new InvalidOperationException("Complete the staged replacement before disposal.");
            }

            return ValueTask.CompletedTask;
        }
    }
}
