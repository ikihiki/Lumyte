using Lumyte.Animation;
using Lumyte.Core.Time;

namespace Lumyte.UI;

/// <summary>A finite immutable transition between drawing poses.</summary>
public sealed class UiVisualTransition
{
    private readonly Tween<UiVisualPose> _tween;

    /// <summary>Initializes a new instance of the <see cref="UiVisualTransition"/> class.</summary>
    /// <param name="from">The validated initial pose.</param>
    /// <param name="to">The validated final pose.</param>
    /// <param name="duration">The nonnegative interpolation interval.</param>
    /// <param name="delay">The nonnegative initial hold interval.</param>
    /// <param name="easing">The temporal easing.</param>
    public UiVisualTransition(UiVisualPose from, UiVisualPose to, Duration duration, Duration delay = default, AnimationEasing easing = AnimationEasing.EaseOut)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        from.Validate();
        to.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(duration.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(delay.Ticks);
        _ = checked(duration.Ticks + delay.Ticks);
        From = from;
        To = to;
        Duration = duration;
        Delay = delay;
        Easing = easing;
        _tween = new(From, To, duration, new PoseInterpolator(), easing);
    }

    /// <summary>Gets the initial pose.</summary>
    public UiVisualPose From { get; }

    /// <summary>Gets the final pose.</summary>
    public UiVisualPose To { get; }

    /// <summary>Gets the interpolation interval.</summary>
    public Duration Duration { get; }

    /// <summary>Gets the hold interval.</summary>
    public Duration Delay { get; }

    /// <summary>Gets the temporal easing.</summary>
    public AnimationEasing Easing { get; }

    internal Duration Total => Duration.FromTicks(checked(Duration.Ticks + Delay.Ticks));

    internal UiVisualPose Sample(Duration elapsed) => elapsed < Delay ? From : _tween.Sample(Duration.FromTicks(Math.Min(Duration.Ticks, (elapsed - Delay).Ticks)));

    internal UiVisualTransition Reduce() => new(new() { Opacity = From.Opacity }, new() { Opacity = To.Opacity }, Duration.FromTicks(Math.Min(Duration.Ticks, TimeSpan.TicksPerMillisecond * 100)), default, Easing);

    private sealed class PoseInterpolator : IAnimationInterpolator<UiVisualPose>
    {
        public UiVisualPose Interpolate(UiVisualPose from, UiVisualPose to, float amount)
        {
            float Lerp(float a, float b) => (float)(a + (((double)b - a) * amount));
            return new() { Opacity = Lerp(from.Opacity, to.Opacity), Offset = new(Lerp(from.Offset.X, to.Offset.X), Lerp(from.Offset.Y, to.Offset.Y)), Scale = new(Lerp(from.Scale.X, to.Scale.X), Lerp(from.Scale.Y, to.Scale.Y)) };
        }
    }
}
