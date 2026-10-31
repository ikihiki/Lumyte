using Lumyte.Core.Time;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

internal sealed class UiPresenceEntry(UiSurface surface, Widget widget)
{
    private bool _initialized;
    private bool _requested;
    private bool _started;
    private bool _awaitingPresentation;
    private bool _notify;
    private TimePoint _start;
    private UiVisualTransition? _transition;

    internal Widget Widget { get; set; } = widget;

    internal UiPresenceState State { get; private set; } = widget.IsPresent ? UiPresenceState.Visible : UiPresenceState.Hidden;

    internal UiVisualPose Pose { get; private set; } = UiVisualPose.Identity;

    internal ulong Generation { get; private set; }

    internal bool Initialized => _initialized;

    internal bool IsGated => IsActive || _awaitingPresentation;

    internal bool IsActive => State is UiPresenceState.Entering or UiPresenceState.Exiting;

    internal bool WantsCapture => IsActive && !_started;

    internal bool NeedsOpacityLayer => _transition is { } transition && (transition.From.Opacity != 1 || transition.To.Opacity != 1);

    internal bool AwaitingPresentation => _awaitingPresentation;

    internal bool HasNotification => _notify;

    internal void Reconcile()
    {
        if (!_initialized && surface.VisualHost is { CanPresent: false })
        {
            return;
        }

        for (Widget? parent = Widget.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.LayoutVisible || parent.Presence?.IsGated == true)
            {
                return;
            }
        }

        bool requested = Widget.IsPresent && Widget.IsVisible && !surface.HostExiting;
        if (_initialized && requested == _requested)
        {
            return;
        }

        bool initial = !_initialized;
        _initialized = true;
        _requested = requested;
        Generation++;
        _notify = false;
        _awaitingPresentation = false;
        UiVisualTransition? definition = requested ? Widget.Transitions?.Enter : Widget.Transitions?.Exit;
        if ((initial && (!requested || Widget.Transitions?.AnimateInitial == false)) || definition is null || !Widget.IsVisible
            || surface.VisualHost is not { } host || host.Motion.Policy == UiMotionPolicy.Disabled)
        {
            Finish(!initial || requested);
            return;
        }

        if (definition.Total.ToTimeSpan() > host.Motion.MaximumVisualLifetime)
        {
            throw new ArgumentException("The effect exceeds the host visual lifetime limit.");
        }

        UiVisualTransition effective = host.Motion.Policy == UiMotionPolicy.Reduced ? definition.Reduce() : definition;
        UiVisualPose from = IsActive ? Pose : effective.From;
        _transition = new(from, effective.To, effective.Duration, effective.Delay, effective.Easing);
        _started = false;
        Pose = from;
        State = requested ? UiPresenceState.Entering : UiPresenceState.Exiting;
        Widget.MarkLayoutDirty();
        surface.PresenceGateChanged();
    }

    internal void Seed(bool requested)
    {
        _initialized = true;
        _requested = requested;
        State = requested ? UiPresenceState.Visible : UiPresenceState.Hidden;
    }

    internal void Consume()
    {
        _initialized = true;
        _requested = Widget.IsPresent && Widget.IsVisible && !surface.HostExiting;
        Generation++;
        Finish(_requested);
    }

    internal void Begin(TimePoint now)
    {
        if (WantsCapture)
        {
            _start = now;
            _started = true;
        }
    }

    internal void Advance(TimePoint now)
    {
        if (!_started || !IsActive || !ParentsReady())
        {
            return;
        }

        Pose = _transition!.Sample(now - _start);
        if (now - _start >= _transition.Total)
        {
            Finish(true);
        }
    }

    internal void Skip() => Finish(_initialized);

    internal void Presented(ulong generation)
    {
        if (generation == Generation && _awaitingPresentation && ParentsReady())
        {
            _awaitingPresentation = false;
            _notify = true;
            surface.PresenceGateChanged();
        }
    }

    internal void Deliver()
    {
        if (_notify && ReferenceEquals(Widget.Surface, surface))
        {
            _notify = false;
            (_requested ? Widget.OnEntered : Widget.OnExited)?.Invoke();
        }
    }

    internal void Suspend()
    {
        if (_initialized)
        {
            _initialized = false;
            Generation++;
            _notify = false;
            _awaitingPresentation = false;
            _started = false;
            _transition = null;
            State = Widget.IsPresent ? UiPresenceState.Visible : UiPresenceState.Hidden;
        }
    }

    internal void Detach()
    {
        Generation++;
        _notify = false;
        _awaitingPresentation = false;
        _started = false;
    }

    private bool ParentsReady()
    {
        for (Widget? parent = Widget.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.LayoutVisible || parent.Presence?.IsGated == true)
            {
                return false;
            }
        }

        return true;
    }

    private void Finish(bool notify)
    {
        bool wasHidden = State == UiPresenceState.Hidden;
        State = _requested ? UiPresenceState.Visible : UiPresenceState.Hidden;
        Pose = UiVisualPose.Identity;
        _transition = null;
        _started = false;
        _awaitingPresentation = notify;
        if (wasHidden != (State == UiPresenceState.Hidden))
        {
            Widget.MarkLayoutDirty();
        }

        surface.PresenceGateChanged();
    }
}
