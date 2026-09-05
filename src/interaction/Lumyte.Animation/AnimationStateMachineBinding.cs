using Lumyte.StateMachine;

namespace Lumyte.Animation;

public sealed class AnimationStateMachineBinding<TContext, TTrigger>
{
    private readonly Dictionary<State<TContext>, IAnimationTimeline> states = [];
    private readonly Dictionary<Transition<TContext, TTrigger>, AnimationTransition> transitions = [];

    public AnimationStateMachineBinding<TContext, TTrigger> Bind(
        State<TContext> state,
        IAnimationTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeline);
        states[state] = timeline;
        return this;
    }

    public AnimationStateMachineBinding<TContext, TTrigger> Bind(
        Transition<TContext, TTrigger> transition,
        AnimationTransition animation)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(animation);
        transitions[transition] = animation;
        return this;
    }

    internal IAnimationTimeline TimelineFor(State<TContext> state) =>
        states.TryGetValue(state, out IAnimationTimeline? timeline)
            ? timeline
            : throw new InvalidOperationException($"Animation state '{state.Name}' has no timeline binding.");

    internal AnimationTransition TransitionFor(Transition<TContext, TTrigger> transition) =>
        transitions.TryGetValue(transition, out AnimationTransition? animation)
            ? animation
            : new AnimationTransition(Lumyte.Core.Time.Duration.Zero);
}
