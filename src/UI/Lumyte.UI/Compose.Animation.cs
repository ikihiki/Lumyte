using Lumyte.Animation;
using Lumyte.Core.Time;
using Lumyte.Signals;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Defines a display value that follows a model through interruptible tweens.</summary>
    /// <typeparam name="T">The exact target property type.</typeparam>
    /// <param name="target">The independent model value.</param>
    /// <param name="duration">The nonnegative duration of each new target transition.</param>
    /// <param name="interpolator">The borrowed pure interpolator.</param>
    /// <param name="easing">The temporal easing.</param>
    /// <param name="onCompleted">Reports the final effective transition after applying its value.</param>
    /// <returns>A passive surface-owned display projection.</returns>
    public static UiAnimated<T> Animate<T>(IReadSignal<T> target, Duration duration, IAnimationInterpolator<T> interpolator, AnimationEasing easing = AnimationEasing.EaseInOut, Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(interpolator);
        ArgumentOutOfRangeException.ThrowIfNegative(duration.Ticks, nameof(duration));
        if (easing is < AnimationEasing.Linear or > AnimationEasing.EaseInOut)
        {
            throw new ArgumentOutOfRangeException(nameof(easing));
        }

        if (target is IUiAnimationProjection)
        {
            throw new ArgumentException("Animation targets must be model values, rather than other display animations.", nameof(target));
        }

        var producer = new UiTargetAnimation<T>(target, duration, interpolator, easing, onCompleted);
        return new(producer, () => producer.Pending, () => producer.Detached);
    }

    /// <summary>Defines automatic playback of an existing immutable timeline.</summary>
    /// <param name="timeline">The borrowed immutable timeline.</param>
    /// <param name="wrapMode">The playback wrapping policy.</param>
    /// <param name="onEvent">Receives crossed markers after all display values have been applied.</param>
    /// <param name="onCompleted">Receives once-mode completion after final display values.</param>
    /// <returns>A passive clip, activated when one of its channels connects.</returns>
    public static UiAnimationClip Animate(AnimationTimeline timeline, AnimationWrapMode wrapMode = AnimationWrapMode.Once, Action<AnimationEventOccurrence>? onEvent = null, Action? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (wrapMode is < AnimationWrapMode.Once or > AnimationWrapMode.Loop)
        {
            throw new ArgumentOutOfRangeException(nameof(wrapMode));
        }

        return new(timeline, wrapMode, onEvent, onCompleted);
    }
}
