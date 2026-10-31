using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

internal sealed class UiPageVisualFlight(UiApplication application, UiVisualCoordinator target, UiVisualCoordinator? source, UiViewVisualPlan? plan, Func<bool> skip)
{
    private readonly List<Pair> _pairs = [];
    private readonly List<UiVisualSnapshot> _images = [];
    private readonly List<Task> _late = [];
    private readonly List<IDisposable> _pairReservations = [];
    private readonly HashSet<Widget> _targets = [];
    private readonly TaskCompletionSource<UiViewVisualOutcome> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly long _preparedAt = application.TimeProvider.GetTimestamp();
    private readonly ulong _epoch = application.GraphicsGeneration;
    private readonly UiWindowSnapshot? _window = target.CaptureWindow();
    private readonly List<UiVisualLayer> _layers = [];
    private readonly HashSet<UiSharedElementKey> _sharedKeys = [];
    private UiVisualSnapshot? _oldPage;
    private UiVisualLayer? _newPage;
    private UiVisualTransition? _enter;
    private UiVisualTransition? _exit;
    private Tween<float>? _connected;
    private IDisposable? _active;
    private TimePoint _start;
    private Duration _elapsed;
    private Duration _total;
    private long _startedAt;
    private bool _started;
    private bool _released;
    private bool _skips;
    private bool _played;
    private bool _attemptedCapture;
    private bool _retirementErrorReported;
    private string _code = "completed";
    private string _skipCode = "capture-unavailable";
    private bool _interrupted;

    internal bool Committed { get; private set; }

    internal bool IsFinal { get; private set; }

    internal Task<UiViewVisualOutcome> Completion => _completion.Task;

    internal async ValueTask PrepareAsync(CancellationToken token)
    {
        if (plan is null || (plan.Enter is null && plan.Exit is null && !plan.Connect))
        {
            Finish("not-configured");
            return;
        }

        if (skip())
        {
            Finish("skipped-by-request");
            return;
        }

        if (application.Motion.Policy == UiMotionPolicy.Disabled)
        {
            Finish("motion-disabled");
            return;
        }

        Duration pageDuration = plan.ExitThenEnter ? (plan.Exit?.Total ?? Duration.Zero) + (plan.Enter?.Total ?? Duration.Zero) : Duration.FromTicks(Math.Max(plan.Exit?.Total.Ticks ?? 0, plan.Enter?.Total.Ticks ?? 0));
        var configured = Duration.FromTicks(Math.Max(pageDuration.Ticks, plan.Connect ? plan.ConnectedDuration.Ticks : 0));
        if (configured.ToTimeSpan() > application.Motion.MaximumVisualLifetime)
        {
            throw new ArgumentException("The planned visual duration exceeds the host limit.");
        }

        if (plan.Enter is null && plan.Exit is null && plan.Connect && (source is null || !Outermost(source.Surface.Root).Keys.Intersect(Outermost(target.Surface.Root).Keys).Any()))
        {
            Finish("no-matches");
            return;
        }

        if (!application.Driver.SupportsVisuals)
        {
            Finish("capture-unavailable");
            return;
        }

        if (configured.Ticks == 0)
        {
            _played = true;
            Finish("completed");
            return;
        }

        if (source is not null)
        {
            ulong sequence = source.PresentedSequence;
            try
            {
                while (source.PresentedSequence == sequence || source.PresentedVisualRevision != source.Surface.VisualRevision)
                {
                    TimeSpan remaining = application.Motion.PreparationTimeout - application.TimeProvider.GetElapsedTime(_preparedAt);
                    if (remaining <= TimeSpan.Zero)
                    {
                        Finish("prepare-timeout");
                        return;
                    }

                    await application.NextTurnAsync().AsTask().WaitAsync(remaining, application.TimeProvider, token);
                }
            }
            catch (TimeoutException)
            {
                Finish("prepare-timeout");
                return;
            }
        }

        _active = application.VisualBudget.BeginTransition();
        if (_active is null)
        {
            Finish("budget-exceeded");
            return;
        }

        bool reduced = application.Motion.Policy == UiMotionPolicy.Reduced;
        _enter = reduced ? plan.Enter?.Reduce() : plan.Enter;
        _exit = source is null ? null : reduced ? plan.Exit?.Reduce() : plan.Exit;
        if (reduced)
        {
            _code = "motion-reduced";
        }

        var oldKeys = new HashSet<Widget>();
        if (!reduced && plan.Connect && source is not null && source.CaptureWindow()?.PresentationRoot == _window?.PresentationRoot)
        {
            Dictionary<UiSharedElementKey, Widget> before = Outermost(source.Surface.Root);
            foreach ((UiSharedElementKey key, Widget next) in Outermost(target.Surface.Root))
            {
                token.ThrowIfCancellationRequested();
                if (skip() || application.TimeProvider.GetElapsedTime(_preparedAt) >= application.Motion.PreparationTimeout)
                {
                    _skips = true;
                    break;
                }

                if (!before.TryGetValue(key, out Widget? previous))
                {
                    _skips = true;
                    continue;
                }

                IDisposable? pairReservation = application.VisualBudget.BeginTransition();
                if (pairReservation is null)
                {
                    _skips = true;
                    continue;
                }

                _pairReservations.Add(pairReservation);
                UiVisualSnapshot? oldImage = null;
                if (!next.HasEquivalentVisualContent(previous))
                {
                    oldImage = await CaptureAsync(source, previous, null, token);
                    _skips |= oldImage is null;
                }

                var layer = new UiVisualLayer(application, target, () => ResolveTarget(key), oldImage is not null, includeAncestors: true);
                _layers.Add(layer);
                if (!await PrepareLayerAsync(layer, token))
                {
                    oldImage?.Dispose();
                    continue;
                }

                _pairs.Add(new(oldImage, layer, UiVisualCoordinator.Bounds(previous)));
                _sharedKeys.Add(key);
                oldKeys.Add(previous);
                _targets.Add(next);
            }

            _connected = new(0, 1, plan.ConnectedDuration, AnimationInterpolators.Float, plan.ConnectedEasing);
        }

        if (_enter is not null || _exit is not null)
        {
            bool opacity = _enter is { } enter && (enter.From.Opacity != 1 || enter.To.Opacity != 1);
            _newPage = new(application, target, () => target.Surface.Root, opacity, ResolveTargets);
            _layers.Add(_newPage);
            bool ready = await PrepareLayerAsync(_newPage, token);
            if (source is not null)
            {
                _oldPage = await CaptureAsync(source, source.Surface.Root, oldKeys, token);
            }

            if (!ready || (source is not null && _oldPage is null))
            {
                _newPage.Dispose();
                _oldPage?.Dispose();
                _newPage = null;
                _oldPage = null;
                _enter = null;
                _exit = null;
                _skips = true;
            }
        }

        _played = _newPage is not null || _pairs.Count != 0;
        if (_played)
        {
            if (_newPage is not null)
            {
                target.Surface.ConsumeInitialPresence();
            }
            else
            {
                foreach (Widget widget in _targets.SelectMany(widget => widget.CollectSubtree()))
                {
                    widget.Presence?.Consume();
                }
            }

            target.Surface.SetPageTransitioning(true);
        }

        _total = plan.ExitThenEnter ? (_exit?.Total ?? Duration.Zero) + (_enter?.Total ?? Duration.Zero) : Duration.FromTicks(Math.Max(_exit?.Total.Ticks ?? 0, _enter?.Total.Ticks ?? 0));
        if (_pairs.Count != 0)
        {
            _total = Duration.FromTicks(Math.Max(_total.Ticks, plan.ConnectedDuration.Ticks));
        }

        if (_newPage is null && _pairs.Count == 0)
        {
            Finish(_skips ? _attemptedCapture ? _skipCode : "no-matches" : reduced ? "motion-reduced" : "no-matches");
        }
    }

    internal void CheckHiddenDeadline()
    {
        TimeSpan elapsed = application.TimeProvider.GetElapsedTime(_preparedAt);
        if (Committed && !IsFinal && elapsed >= application.Motion.PreparationTimeout && elapsed - application.Motion.PreparationTimeout >= application.Motion.MaximumVisualLifetime)
        {
            Finish("presentation-changed", true);
        }
    }

    internal void Commit() => Committed = true;

    internal void Advance(bool available, UiWindowSnapshot? state)
    {
        if (!Committed || IsFinal)
        {
            return;
        }

        if (skip())
        {
            Finish("skipped-by-request");
            return;
        }

        if (!available || state?.Scale != _window?.Scale || state?.ClientSize != _window?.ClientSize || application.GraphicsGeneration != _epoch
            || _pairs.Any(pair => pair.Layer.CurrentBounds is null))
        {
            Finish("presentation-changed", true);
            return;
        }

        _newPage?.Advance();
        foreach (Pair pair in _pairs)
        {
            pair.Layer.Advance();
        }

        if (_newPage?.Failed == true || _pairs.Any(pair => pair.Layer.Failed))
        {
            Finish("capture-unavailable", true);
            return;
        }

        if (!_started)
        {
            _started = true;
            _start = application.AnimationClock.Now;
            _startedAt = application.TimeProvider.GetTimestamp();
        }

        if (application.TimeProvider.GetElapsedTime(_startedAt) >= application.Motion.MaximumVisualLifetime)
        {
            Finish("presentation-changed", true);
        }
        else
        {
            _elapsed = application.AnimationClock.Now - _start;
            if (_elapsed >= _total)
            {
                Finish(_skips ? "completed-with-skips" : _code);
            }
        }
    }

    internal void Record(DrawingCanvas2D canvas, UiVisualCoordinator coordinator)
    {
        if (_newPage is null)
        {
            target.Surface.RecordNormalVisualPage(canvas, ResolveTargets());
        }
        else
        {
            Rect2D bounds = new(0, 0, target.Surface.Root.Size.X, target.Surface.Root.Size.Y);
            if (_oldPage is not null)
            {
                coordinator.Draw(canvas, _oldPage, bounds, _exit?.Sample(_elapsed) ?? UiVisualPose.Identity);
            }

            Duration enterAt = plan?.ExitThenEnter == true ? _exit?.Total ?? Duration.Zero : Duration.Zero;
            if (_elapsed >= enterAt)
            {
                _newPage.Draw(canvas, bounds, _enter?.Sample(_elapsed - enterAt) ?? UiVisualPose.Identity);
            }
        }

        float amount = _connected?.Sample(Duration.FromTicks(Math.Min(_elapsed.Ticks, plan!.ConnectedDuration.Ticks))) ?? 1;
        foreach (Pair pair in _pairs)
        {
            float Lerp(float a, float b) => (float)(a + (((double)b - a) * amount));
            if (pair.Layer.CurrentBounds is not { } destination)
            {
                continue;
            }

            var bounds = new Rect2D(Lerp(pair.From.X, destination.X), Lerp(pair.From.Y, destination.Y), Lerp(pair.From.Width, destination.Width), Lerp(pair.From.Height, destination.Height));
            if (pair.Old is { } old && pair.Layer.Image is { } current)
            {
                coordinator.DrawConnected(canvas, old, current, bounds, amount);
            }
            else
            {
                pair.Layer.Draw(canvas, bounds, UiVisualPose.Identity);
            }
        }
    }

    internal void Presented()
    {
        if (Committed && IsFinal && !_completion.Task.IsCompleted)
        {
            target.Surface.SetPageTransitioning(false);
            _completion.TrySetResult(new(_code, _interrupted, _played && _code != "skipped-by-request"));
            ReleaseImages();
        }
    }

    internal void Stop()
    {
        Finish("stopped", true);
        target.Surface.SetPageTransitioning(false);
        _completion.TrySetResult(new("stopped", true));
        ReleaseImages();
    }

    internal async ValueTask DrainAsync()
    {
        ReleaseImages();
        await application.Driver.DrainVisualsAsync();
        try
        {
            await Task.WhenAll(_late);
        }
        catch (Exception error)
        {
            if (!_retirementErrorReported)
            {
                _retirementErrorReported = true;
                application.FailInput(error);
            }
        }

        await application.Driver.DrainVisualsAsync();
        await Task.WhenAll(_images.Select(image => image.Released));
        foreach (UiVisualLayer layer in _layers)
        {
            await layer.DrainAsync();
        }

        _layers.Clear();
        _sharedKeys.Clear();
        source = null;
        skip = static () => false;
        plan = null;
        _pairs.Clear();
        _targets.Clear();
        _images.Clear();
        _late.Clear();
        _newPage = null;
        _oldPage = null;
    }

    private static Dictionary<UiSharedElementKey, Widget> Outermost(Widget root)
    {
        var result = new Dictionary<UiSharedElementKey, Widget>();
        foreach (Widget widget in root.CollectSubtree())
        {
            if (widget.SharedElementKey is not { } key || !widget.LayoutVisible || widget.Size.X <= 0 || widget.Size.Y <= 0)
            {
                continue;
            }

            bool nested = false;
            for (Widget? parent = widget.Parent; parent is not null; parent = parent.Parent)
            {
                nested |= parent.SharedElementKey is not null;
            }

            if (!nested)
            {
                result.Add(key, widget);
            }
        }

        return result;
    }

    private static async Task RetireLateAsync(Task<UiVisualSnapshot?> task)
    {
        UiVisualSnapshot? image = await task;
        image?.Dispose();
        if (image is not null)
        {
            await image.Released;
        }
    }

    private Widget? ResolveTarget(UiSharedElementKey key) => Outermost(target.Surface.Root).GetValueOrDefault(key) is { IsPresent: true } widget ? widget : null;

    private IReadOnlySet<Widget> ResolveTargets() => Outermost(target.Surface.Root).Where(pair => _sharedKeys.Contains(pair.Key)).Select(pair => pair.Value).ToHashSet();

    private async ValueTask<bool> PrepareLayerAsync(UiVisualLayer layer, CancellationToken token)
    {
        _attemptedCapture = true;
        TimeSpan remaining = application.Motion.PreparationTimeout - application.TimeProvider.GetElapsedTime(_preparedAt);
        if (remaining <= TimeSpan.Zero)
        {
            _skips = true;
            _skipCode = "prepare-timeout";
            layer.Dispose();
            return false;
        }

        using var deadline = new CancellationTokenSource(remaining, application.TimeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, token);
        try
        {
            await layer.PrepareAsync(linked.Token);
            if (!layer.Failed)
            {
                return true;
            }

            _skipCode = layer.FailureCode;
        }
        catch (TimeoutException)
        {
            _skipCode = "prepare-timeout";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            _skipCode = "prepare-timeout";
        }

        _skips = true;
        layer.Dispose();
        return false;
    }

    private async ValueTask<UiVisualSnapshot?> CaptureAsync(UiVisualCoordinator coordinator, Widget widget, IReadOnlySet<Widget>? exclude, CancellationToken token)
    {
        TimeSpan remaining = application.Motion.PreparationTimeout - application.TimeProvider.GetElapsedTime(_preparedAt);
        if (remaining <= TimeSpan.Zero)
        {
            _skipCode = "prepare-timeout";
            _skips = true;
            return null;
        }

        ulong revision = widget.VisualRevision;
        Rect2D bounds = UiVisualCoordinator.Bounds(widget);
        _attemptedCapture = true;
        Task<UiVisualSnapshot?> task = coordinator.CaptureAsync(widget, exclude, true);
        string failureCode = coordinator.CaptureFailureCode;
        try
        {
            UiVisualSnapshot? image = await task.WaitAsync(remaining, application.TimeProvider, token);
            if (image is not null)
            {
                _images.Add(image);
                if (!ReferenceEquals(widget.Surface, coordinator.Surface) || widget.VisualRevision != revision || UiVisualCoordinator.Bounds(widget) != bounds)
                {
                    image.Dispose();
                    _skips = true;
                    return null;
                }
            }

            if (image is null)
            {
                _skips = true;
                if (_skipCode != "prepare-timeout")
                {
                    _skipCode = failureCode;
                }
            }

            return image;
        }
        catch (TimeoutException)
        {
            _skipCode = "prepare-timeout";
            _late.Add(RetireLateAsync(task));
            _skips = true;
            return null;
        }
        catch (OperationCanceledException)
        {
            _late.Add(RetireLateAsync(task));
            throw;
        }
    }

    private void Finish(string code, bool interrupted = false)
    {
        IsFinal = true;
        _code = code;
        _interrupted = interrupted;
        _active?.Dispose();
        _active = null;
        foreach (IDisposable reservation in _pairReservations)
        {
            reservation.Dispose();
        }

        _pairReservations.Clear();
        ReleaseImages();
    }

    private void ReleaseImages()
    {
        if (!_released)
        {
            _released = true;
            foreach (UiVisualSnapshot image in _images)
            {
                image.Dispose();
            }

            foreach (UiVisualLayer layer in _layers)
            {
                layer.Dispose();
            }
        }
    }

    private sealed record Pair(UiVisualSnapshot? Old, UiVisualLayer Layer, Rect2D From);
}
