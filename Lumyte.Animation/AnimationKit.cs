using Lumyte.Core.Time;
using Lumyte.StateMachine;

namespace Lumyte.Animation;

public static partial class AnimationKit
{
    public static AnimationChannel<T> Channel<T>(string name) => new(name);

    public static Keyframe<T> Keyframe<T>(Duration time, T value) => new(time, value);

    public static SequenceTimeline Sequence(params IReadOnlyList<IAnimationTimeline> children) => new(children);

    public static ParallelTimeline Parallel(params IReadOnlyList<IAnimationTimeline> children) => new(children);

    public static DelayTimeline Delay(IAnimationTimeline child, Duration delay) => new(child, delay);

    public static RepeatTimeline Repeat(IAnimationTimeline child, int count) => new(child, count);

    public static ReverseTimeline Reverse(IAnimationTimeline child) => new(child);

    public static AnimationStatePart<TContext> Animate<TContext>(
        State<TContext> state,
        IAnimationTimeline timeline) =>
        new(state, timeline);

    public static AnimationTransitionPart<TContext, TTrigger> Animate<TContext, TTrigger>(
        Transition<TContext, TTrigger> transition) =>
        new(transition);
}
