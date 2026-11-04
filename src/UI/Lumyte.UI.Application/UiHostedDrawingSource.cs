using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Standard application compositor. The host serializes frames and calls CompleteFrame only after GPU completion/retirement.</summary>
internal sealed partial class UiHostedDrawingSource(Func<UiWindowSourceSnapshot> capture) : IUiWindowSource, IUiWindowPresentationStager
{
    private readonly List<Presentation> _presentations = [];

    private readonly Dictionary<Presentation, UiWindowPresentationView> _recorded = [];

    private readonly HashSet<UiSurface> _paused = [];

    private readonly Dictionary<Presentation, (UiPresenceEntry Entry, ulong Generation)[]> _visualPresentations = [];
    private readonly Dictionary<Presentation, bool> _ordinaryVisualFrames = [];
    private readonly Dictionary<Presentation, ulong> _visualRevisions = [];
    private TaskCompletionSource? _frame;

    public Guid SourceId { get; } = Guid.NewGuid();

    internal Action<Lumyte.UI.Compose.Definitions.Widget>? ValidateRoot { get; set; }

    internal Action<UiSurface>? ValidateSurface { get; set; }

    internal Func<DrawList2D, CancellationToken, ValueTask>? PrepareCandidate { get; set; }

    public UiWindowSourceSnapshot CaptureState() => capture();

    public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PrepareSurface(surface);
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
        BeginFrame();
        try
        {
            FrameItem[] items = PrepareFrame();
            RecordItems(canvas, items);
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
                (token.Surface.VisualHost as UiVisualCoordinator)?.Presented(view, _ordinaryVisualFrames.GetValueOrDefault(token), _visualRevisions.GetValueOrDefault(token));
                if (!token.Surface.PageTransitioning && _visualPresentations.TryGetValue(token, out (UiPresenceEntry Entry, ulong Generation)[]? entries))
                {
                    token.Surface.ConfirmVisualPresentation(entries);
                }

                token.Presented = view;
                token.PresentedGeometryRevision = view.GeometryRevision;
            }
        }

        _frame = null;
        _recorded.Clear();
        _visualPresentations.Clear();
        _ordinaryVisualFrames.Clear();
        _visualRevisions.Clear();
        frame.TrySetResult();
    }

    internal UiVisualSnapshot[] RecordedVisuals() => _recorded.Where(pair => pair.Value.IsVisible).Select(pair => pair.Key.Surface.VisualHost).OfType<UiVisualCoordinator>().SelectMany(coordinator => coordinator.Recorded).Distinct().ToArray();

    internal UiWindowPresentationView? Recorded(UiSurface surface) => _recorded.FirstOrDefault(pair => ReferenceEquals(pair.Key.Surface, surface)).Value;

    internal UiWindowPresentationView Presented(UiWindowId window) => _presentations.Single(p => p.View.Window == window).Presented ?? throw new InvalidOperationException("No frame has been presented.");

    private void BeginFrame()
    {
        if (_frame is not null)
        {
            throw new InvalidOperationException("Finish the previous frame before recording another.");
        }

        _frame = new();
        _recorded.Clear();
        _visualPresentations.Clear();
        _ordinaryVisualFrames.Clear();
        _visualRevisions.Clear();
    }

    private void RecordItems(DrawingCanvas2D canvas, FrameItem[] items)
    {
        foreach (FrameItem item in items)
        {
            Presentation presentation = item.Presentation;
            UiWindowPresentationView view = item.View;
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
                (presentation.Surface.VisualHost as UiVisualCoordinator)?.BeginRecord();
                presentation.Surface.Record(canvas);
                CapturePresentation(presentation);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }

    private void CapturePresentation(Presentation presentation)
    {
        _visualPresentations[presentation] = presentation.Surface.CaptureVisualPresentation();
        _visualRevisions[presentation] = presentation.Surface.VisualRevision;
        _ordinaryVisualFrames[presentation] = presentation.Surface.VisualHost is not UiVisualCoordinator { Page: { Committed: true, IsFinal: false } };
    }

    private sealed class Presentation(UiHostedDrawingSource source, UiSurface surface, UiWindowPresentationView view) : IUiWindowPresentation
    {
        private Task? _retirement;

        public ulong PresentedGeometryRevision { get; set; }

        internal UiHostedDrawingSource Source => source;

        internal bool Active { get; set; } = true;

        internal UiSurface Surface { get; } = surface;

        internal ulong EligibilityRevision { get; set; } = ulong.MaxValue;

        internal bool CacheEligible { get; set; }

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

            source.DiscardCache();
            source._presentations.Remove(this);
            source._paused.Remove(Surface);
        }
    }

    private sealed class Staged(UiHostedDrawingSource source, Presentation? current, UiSurface surface, UiWindowPresentationView view) : IUiStagedWindowPresentation
    {
        private Presentation? _previous = current;
        private Presentation? _candidate;
        private object? _inputVersion;
        private UiSignalRevision _signalRevision;
        private bool _committed;
        private bool _discarded;
        private Lumyte.UI.Compose.Definitions.Widget? _root;
        private UiWindowSourceSnapshot? _state;

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.PrepareSurface(surface);
            _root = surface.Root;
            _inputVersion = surface.InputVersionForWindowing;
            _signalRevision = surface.CommittedSignalRevision;
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
            return _candidate is not null && !_committed && !_discarded && ReferenceEquals(surface.Root, _root) && ReferenceEquals(surface.InputVersionForWindowing, _inputVersion) && surface.SignalRevision == _signalRevision && source.CaptureState() == _state;
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
