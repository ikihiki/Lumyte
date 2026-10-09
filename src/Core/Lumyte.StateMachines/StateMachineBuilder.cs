namespace Lumyte.StateMachines;

/// <summary>Compiles immutable definitions or ready execution instances from typed states and transitions.</summary>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <typeparam name="TTrigger">The trigger value type.</typeparam>
public sealed class StateMachineBuilder<TContext, TTrigger>
{
    private readonly State<TContext> _initialState;
    private readonly List<State<TContext>> _states = [];
    private readonly List<Transition<TContext, TTrigger>> _transitions = [];

    /// <summary>Initializes a new instance of the <see cref="StateMachineBuilder{TContext, TTrigger}"/> class.</summary>
    /// <param name="initialState">The initial state.</param>
    public StateMachineBuilder(State<TContext> initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        _initialState = initialState;
    }

    /// <summary>Registers a state for subsequent definition compilation.</summary>
    /// <param name="state">The state.</param>
    public void AddState(State<TContext> state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states.Add(state);
    }

    /// <summary>Registers an ordered transition for subsequent definition compilation.</summary>
    /// <param name="transition">The transition.</param>
    public void AddTransition(Transition<TContext, TTrigger> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        _transitions.Add(transition);
    }

    /// <summary>Validates the definition, creates an independent execution instance and runs its initial entry actions.</summary>
    /// <param name="context">The context.</param>
    /// <returns>The computed result.</returns>
    public StateMachineInstance<TContext, TTrigger> Build(TContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return new StateMachineInstance<TContext, TTrigger>(BuildDefinition(), context);
    }

    /// <summary>Validates, snapshots and freezes a reusable definition without executing entry actions.</summary>
    /// <returns>The computed result.</returns>
    public StateMachine<TContext, TTrigger> BuildDefinition()
    {
        var states = new List<State<TContext>>
        {
            _initialState,
        };
        foreach (Transition<TContext, TTrigger> transition in _transitions)
        {
            AddDistinct(states, transition.From);
            AddDistinct(states, transition.To);
        }

        foreach (State<TContext> state in _states)
        {
            AddDistinct(states, state);
        }

        return new StateMachine<TContext, TTrigger>(_initialState, [.. states], [.. _transitions]);
    }

    private static void AddDistinct(List<State<TContext>> states, State<TContext> state)
    {
        if (!states.Contains(state))
        {
            states.Add(state);
        }
    }
}
