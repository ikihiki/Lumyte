using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed record AnimationTransition
{
    public AnimationTransition(
        AnimationState from,
        AnimationState to,
        string trigger,
        Duration duration,
        AnimationBlend? blend = null,
        ICurve? curve = null)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        ArgumentException.ThrowIfNullOrWhiteSpace(trigger);
        if (duration < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Transition duration cannot be negative.");
        }

        if (duration > Duration.Zero && blend is null)
        {
            throw new ArgumentNullException(nameof(blend), "A timed transition requires blend operations.");
        }

        Trigger = trigger;
        Duration = duration;
        Blend = blend;
        Curve = curve ?? Curves.Linear;
    }

    public AnimationState From { get; }

    public AnimationState To { get; }

    public string Trigger { get; }

    public Duration Duration { get; }

    public AnimationBlend? Blend { get; }

    public ICurve Curve { get; }
}
