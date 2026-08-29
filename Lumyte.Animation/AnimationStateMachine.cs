namespace Lumyte.Animation;

public sealed class AnimationStateMachine
{
    private readonly AnimationState[] states;
    private readonly AnimationTransition[] transitions;

    public AnimationStateMachine(
        AnimationState initialState,
        IReadOnlyList<AnimationState> states,
        IReadOnlyList<AnimationTransition> transitions)
    {
        InitialState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(transitions);
        this.states = [.. states];
        this.transitions = [.. transitions];
        if (!this.states.Contains(initialState))
        {
            throw new ArgumentException("The initial state must belong to the state machine.", nameof(initialState));
        }

        if (this.states.Select(state => state.Name).Distinct(StringComparer.Ordinal).Count() != this.states.Length)
        {
            throw new ArgumentException("Animation state names must be unique.", nameof(states));
        }

        if (this.transitions.Any(transition =>
            !this.states.Contains(transition.From) || !this.states.Contains(transition.To)))
        {
            throw new ArgumentException("Every transition state must belong to the state machine.", nameof(transitions));
        }

        if (this.transitions
            .GroupBy(transition => (transition.From, transition.Trigger))
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException("A state can define only one transition for each trigger.", nameof(transitions));
        }
    }

    public AnimationState InitialState { get; }

    public IReadOnlyList<AnimationState> States => states;

    public IReadOnlyList<AnimationTransition> Transitions => transitions;

    internal AnimationTransition? FindTransition(AnimationState state, string trigger) =>
        transitions.SingleOrDefault(transition =>
            transition.From == state
            && string.Equals(transition.Trigger, trigger, StringComparison.Ordinal));
}
