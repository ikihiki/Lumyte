namespace Lumyte.StateMachines;

/// <summary>An immutable state and transition definition shared by independent execution instances.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <typeparam name="TTrigger">The trigger value type.</typeparam>
public sealed class StateMachine<TContext, TTrigger>
{
    internal StateMachine(State<TContext> initialState, State<TContext>[] states, Transition<TContext, TTrigger>[] transitions)
    {
        InitialState = initialState;
        States = Array.AsReadOnly(states);
        Transitions = Array.AsReadOnly(transitions);
        var candidates = new Dictionary<State<TContext>, List<Transition<TContext, TTrigger>>>(ReferenceEqualityComparer.Instance);
        foreach (Transition<TContext, TTrigger> transition in transitions.OrderByDescending(item => item.Priority))
        {
            if (!candidates.TryGetValue(transition.From, out List<Transition<TContext, TTrigger>>? outgoing))
            {
                outgoing = [];
                candidates.Add(transition.From, outgoing);
            }

            outgoing.Add(transition);
        }

        var transitionsByState = new Dictionary<State<TContext>, Candidate[]>(states.Length, ReferenceEqualityComparer.Instance);
        foreach (State<TContext> state in states)
        {
            transitionsByState.Add(state, candidates.TryGetValue(state, out List<Transition<TContext, TTrigger>>? outgoing) ? new Candidate[outgoing.Count] : []);
        }

        // Allocate every state's array before linking targets, including cycles and self-transitions.
        foreach (KeyValuePair<State<TContext>, List<Transition<TContext, TTrigger>>> entry in candidates)
        {
            Candidate[] outgoing = transitionsByState[entry.Key];
            for (int index = 0; index < outgoing.Length; index++)
            {
                Transition<TContext, TTrigger> transition = entry.Value[index];
                outgoing[index] = new Candidate(transition, transitionsByState[transition.To]);
            }
        }

        InitialCandidates = transitionsByState[initialState];
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

    internal Candidate[] InitialCandidates { get; }

    internal static int Find(Candidate[] candidates, TTrigger trigger, TContext context)
    {
        for (int index = 0; index < candidates.Length; index++)
        {
            Transition<TContext, TTrigger> transition = candidates[index].Transition;
            if (EqualityComparer<TTrigger>.Default.Equals(trigger, transition.Trigger) && transition.CanTake(context))
            {
                return index;
            }
        }

        return -1;
    }

    internal static int FindAny(Candidate[] candidates, List<TTrigger> triggers, TContext context)
    {
        for (int index = 0; index < candidates.Length; index++)
        {
            Transition<TContext, TTrigger> transition = candidates[index].Transition;
            if (triggers.Contains(transition.Trigger) && transition.CanTake(context))
            {
                return index;
            }
        }

        return -1;
    }

    internal readonly record struct Candidate(Transition<TContext, TTrigger> Transition, Candidate[] TargetCandidates);
}
