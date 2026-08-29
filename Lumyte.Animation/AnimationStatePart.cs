using Lumyte.StateMachine;

namespace Lumyte.Animation;

public sealed class AnimationStatePart<TContext> : IAnimationMachinePart
{
    internal AnimationStatePart(State<TContext> state, IAnimationTimeline timeline)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
    }

    internal State<TContext> State { get; }

    internal IAnimationTimeline Timeline { get; }
}
