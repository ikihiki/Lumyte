using Lumyte.Core.Time;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication
{
    private readonly CapturedAnimationClock _capturedAnimationClock = new();
    private IMonotonicClock? _hostAnimationClock;
    private long _lastAnimationFrame;
    private bool _animatedFrame;

    internal IMonotonicClock AnimationClock => _capturedAnimationClock;

    internal bool BeginAnimationFrame()
    {
        if (State is UiApplicationState.Stopping or UiApplicationState.StopFailed or UiApplicationState.Stopped)
        {
            return false;
        }

        _hostAnimationClock ??= _options.AnimationClock ?? new ProviderAnimationClock(TimeProvider);
        _capturedAnimationClock.Value = _hostAnimationClock.Now;
        long now = TimeProvider.GetTimestamp();
        bool due = !_animatedFrame || TimeProvider.GetElapsedTime(_lastAnimationFrame, now) >= _options.FrameInterval;
        if (due)
        {
            _lastAnimationFrame = now;
            _animatedFrame = true;
        }

        return due;
    }

    private void AdvancePopupAnimations(bool due)
    {
        if (_popups is null)
        {
            return;
        }

        foreach (Lumyte.UI.Windowing.UiPopupInfo popup in _popups.CapturePopups())
        {
            if (!_popups.TryGetSurface(popup.Id, out UiSurface? surface))
            {
                continue;
            }

            surface!.SetAnimationClock(_capturedAnimationClock);
            bool visible = State == UiApplicationState.Running && Driver.CanAnimate && _popups.CanAnimate(popup.Id);
            surface.SetAnimationUpdatesEnabled(visible);
            if (due || surface.HasPendingAnimationUpdates)
            {
                surface.AdvanceAnimations();
            }

            surface.UpdateLayout();
            (surface.VisualHost as UiVisualCoordinator)?.Advance();
        }
    }

    private sealed class CapturedAnimationClock : IMonotonicClock
    {
        public TimePoint Now => Value;

        internal TimePoint Value { get; set; }
    }

    private sealed class ProviderAnimationClock(TimeProvider provider) : IMonotonicClock
    {
        private readonly long _origin = provider.GetTimestamp();

        public TimePoint Now => TimePoint.FromTicks(provider.GetElapsedTime(_origin, provider.GetTimestamp()).Ticks);
    }
}
