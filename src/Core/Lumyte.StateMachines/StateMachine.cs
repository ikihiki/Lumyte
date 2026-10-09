namespace Lumyte.StateMachines;

/// <summary>An immutable state and transition definition shared by independent execution instances.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <typeparam name="TTrigger">The trigger value type.</typeparam>
public sealed class StateMachine<TContext, TTrigger>
{
    private readonly Transition<TContext, TTrigger>[] _orderedTransitions;

    internal StateMachine(State<TContext> initialState, State<TContext>[] states, Transition<TContext, TTrigger>[] transitions)
    {
        InitialState = initialState;
        States = Array.AsReadOnly(states);
        Transitions = Array.AsReadOnly(transitions);
        _orderedTransitions = [.. transitions.OrderByDescending(item => item.Priority)];
        foreach (State<TContext> state in states)
        {
            state.Freeze();
        }

        foreach (Transition<TContext, TTrigger> transition in transitions)
        {
            transition.Freeze();
        }
    }

    /// <summary>Gets the initial state.</summary>
    public State<TContext> InitialState { get; }

    /// <summary>Gets the states.</summary>
    public IReadOnlyList<State<TContext>> States { get; }

    /// <summary>Gets the transitions.</summary>
    public IReadOnlyList<Transition<TContext, TTrigger>> Transitions { get; }

    internal Transition<TContext, TTrigger>? Find(State<TContext> state, TTrigger trigger, TContext context)
    {
        foreach (Transition<TContext, TTrigger> transition in _orderedTransitions)
        {
            if (ReferenceEquals(state, transition.From) && EqualityComparer<TTrigger>.Default.Equals(trigger, transition.Trigger) && transition.CanTake(context))
            {
                return transition;
            }
        }

        return null;
    }

    internal Transition<TContext, TTrigger>? FindAny(State<TContext> state, List<TTrigger> triggers, TContext context)
    {
        foreach (Transition<TContext, TTrigger> transition in _orderedTransitions)
        {
            if (ReferenceEquals(state, transition.From) && triggers.Contains(transition.Trigger) && transition.CanTake(context))
            {
                return transition;
            }
        }

        return null;
    }
}
