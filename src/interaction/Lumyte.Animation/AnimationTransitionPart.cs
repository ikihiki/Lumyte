using Lumyte.Core.Time;
using Lumyte.StateMachine;

namespace Lumyte.Animation;

public sealed class AnimationTransitionPart<TContext, TTrigger> : IAnimationMachinePart
{
    private bool frozen;

    internal AnimationTransitionPart(Transition<TContext, TTrigger> transition)
    {
        Transition = transition ?? throw new ArgumentNullException(nameof(transition));
        Animation = new AnimationTransition(Duration.Zero);
    }

    internal Transition<TContext, TTrigger> Transition { get; }

    internal AnimationTransition Animation { get; private set; }

    public AnimationTransitionPart<TContext, TTrigger> Crossfade(
        Duration duration,
        AnimationBlend blend,
        ICurve? curve = null)
    {
        if (frozen)
        {
            throw new InvalidOperationException(
                "An animation transition cannot be changed after it belongs to a machine.");
        }

        Animation = new AnimationTransition(duration, blend, curve);
        return this;
    }

    internal void Freeze() => frozen = true;
}
