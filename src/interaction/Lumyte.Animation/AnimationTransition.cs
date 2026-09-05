using Lumyte.Core.Time;

namespace Lumyte.Animation;

public sealed record AnimationTransition
{
    public AnimationTransition(
        Duration duration,
        AnimationBlend? blend = null,
        ICurve? curve = null)
    {
        if (duration < Duration.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Transition duration cannot be negative.");
        }

        if (duration > Duration.Zero && blend is null)
        {
            throw new ArgumentNullException(nameof(blend), "A timed transition requires blend operations.");
        }

        Duration = duration;
        Blend = blend;
        Curve = curve ?? Curves.Linear;
    }

    public Duration Duration { get; }

    public AnimationBlend? Blend { get; }

    public ICurve Curve { get; }
}
