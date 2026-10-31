using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;

namespace Lumyte.UI;

internal sealed class UiTargetAnimation<T>(IReadSignal<T> target, Duration duration, IAnimationInterpolator<T> interpolator, AnimationEasing easing, Action? completed) : UiAnimationProducer
{
    private readonly IReadSignal<T> _target = target;
    private readonly Duration _duration = duration;
    private readonly IAnimationInterpolator<T> _interpolator = interpolator;
    private readonly AnimationEasing _easing = easing;
    private SignalEffect? _watch;
    private Target? _latest;
    private Target? _applied;
    private Target? _stagedTarget;
    private T _goal = default!;
    private T _current = default!;
    private T _pending = default!;
    private Tween<T>? _tween;
    private TimePoint _start;
    private bool _inherited;
    private bool _finishing;
    private bool _completionPending;

    internal override ulong Revision => _watch?.Revision ?? 0;

    internal override bool HasPending => _watch?.Failure is not null || _completionPending || !ReferenceEquals(Volatile.Read(ref _latest), _applied);

    internal override bool IsRunning => _tween is not null;

    internal T Pending => _pending;

    internal T Detached => _target.Peek();

    internal override bool Inherit(UiAnimationProducer previous)
    {
        if (previous is not UiTargetAnimation<T> old || !ReferenceEquals(_target, old._target) || _duration != old._duration
            || !ReferenceEquals(_interpolator, old._interpolator) || _easing != old._easing)
        {
            return false;
        }

        _goal = old._goal;
        _current = old._current;
        _pending = _current;
        _tween = old._tween;
        _start = old._start;
        _completionPending = old._completionPending;
        _inherited = true;
        InheritClock(old);
        return true;
    }

    protected override void ConnectCore()
    {
        Scope.Run(() => _watch = Signal.Effect(() => Volatile.Write(ref _latest, new(_target.Value)), onError: _ => { }));
        if (!_inherited)
        {
            _goal = _latest!.Value;
            _current = _goal;
            _pending = _goal;
            _tween = null;
            _completionPending = false;
            _applied = _latest;
        }

        _inherited = false;
    }

    protected override void EvaluateCore()
    {
        if (_watch!.Failure is { } failure)
        {
            throw new InvalidOperationException("The animation target failed.", failure);
        }

        _stagedTarget = Volatile.Read(ref _latest)!;
        T latest = _stagedTarget.Value;
        T current = Sample();
        if (!EqualityComparer<T>.Default.Equals(latest, _goal))
        {
            RevokeNotifications();
            _completionPending = false;
            _goal = latest;
            _tween = new(current, latest, _duration, _interpolator, _easing);
            _start = ActiveTime;
            current = Sample();
        }

        _pending = current;
        _finishing = _tween is not null && ActiveTime - _start >= _duration;
        if (completed is not null && (_finishing || _completionPending))
        {
            Notify(DeliverCompletion);
        }
    }

    protected override void CommitCore()
    {
        _current = _pending;
        _applied = _stagedTarget;
        if (_finishing)
        {
            _tween = null;
            _finishing = false;
            _completionPending = completed is not null;
        }
    }

    protected override void DisconnectCore()
    {
        _watch = null;
        _latest = null;
        _tween = null;
    }

    private void DeliverCompletion()
    {
        if (!ReferenceEquals(Volatile.Read(ref _latest), _applied))
        {
            return;
        }

        _completionPending = false;
        completed?.Invoke();
    }

    private T Sample() => _tween is null ? _current : _tween.Sample(Duration.FromTicks(Math.Min(_duration.Ticks, (ActiveTime - _start).Ticks)));

    private sealed record Target(T Value);
}
