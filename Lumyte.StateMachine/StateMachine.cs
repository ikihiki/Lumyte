using Lumyte.Composition;

namespace Lumyte.StateMachine;

[Composable(Factory = "StateMachineKit", Name = "Machine")]
public sealed partial class StateMachine<TContext, TTrigger>
{
    private State<TContext> initialState = null!;
    private Transition<TContext, TTrigger>[] transitions = [];
    private State<TContext>[] states = [];

    [ComposeParameter]
    public required State<TContext> InitialState
    {
        get => initialState;
        init
        {
            initialState = value ?? throw new ArgumentNullException(nameof(value));
            initialState.Freeze();
        }
    }

    public IReadOnlyList<State<TContext>> States => states;

    public IReadOnlyList<Transition<TContext, TTrigger>> Transitions => transitions;

    [ComposeContent]
    private IReadOnlyList<Transition<TContext, TTrigger>> ComposedTransitions
    {
        get => transitions;
        set
        {
            if (transitions.Length != 0)
            {
                throw new InvalidOperationException("A state machine definition can be composed only once.");
            }

            Transition<TContext, TTrigger>[] candidate = [.. value];
            Validate(candidate);
            transitions = candidate;
            states =
            [
                InitialState,
                .. candidate.SelectMany(transition => new[] { transition.From, transition.To }),
            ];
            states = [.. states.Distinct()];
            foreach (State<TContext> state in states)
            {
                state.Freeze();
            }

            foreach (Transition<TContext, TTrigger> transition in transitions)
            {
                transition.Freeze();
            }
        }
    }

    public StateMachineInstance<TContext, TTrigger> CreateInstance(TContext context) =>
        new(this, context);

    internal Transition<TContext, TTrigger>? FindTransition(
        State<TContext> state,
        TTrigger trigger,
        TContext context) =>
        transitions
            .Select((transition, index) => (transition, index))
            .Where(candidate =>
                ReferenceEquals(candidate.transition.From, state)
                && EqualityComparer<TTrigger>.Default.Equals(candidate.transition.Trigger, trigger)
                && candidate.transition.CanTake(context))
            .OrderByDescending(candidate => candidate.transition.Priority)
            .ThenBy(candidate => candidate.index)
            .Select(candidate => candidate.transition)
            .FirstOrDefault();

    private void Validate(IReadOnlyList<Transition<TContext, TTrigger>> candidate)
    {
        if (candidate.Any(transition => transition is null))
        {
            throw new ArgumentException("A state machine cannot contain a null transition.", nameof(candidate));
        }
    }
}
