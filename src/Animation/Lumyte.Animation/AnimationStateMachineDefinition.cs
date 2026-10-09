using Lumyte.StateMachines;

namespace Lumyte.Animation;

/// <summary>An immutable generic control definition and one-to-one animation state bindings.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
public sealed class AnimationStateMachineDefinition<TState, TContext>
    where TState : notnull
{
    /// <summary>Initializes a new instance of the <see cref="AnimationStateMachineDefinition{TState, TContext}"/> class.</summary>
    /// <param name="control">The control.</param>
    /// <param name="states">The states.</param>
    /// <param name="automaticTrigger">The automatic trigger.</param>
    public AnimationStateMachineDefinition(StateMachine<AnimationStateContext<TContext>, StateMachineTrigger> control, IReadOnlyList<AnimationStateBinding<TState, TContext>> states, StateMachineTrigger? automaticTrigger = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(states);
        AnimationStateBinding<TState, TContext>[] bindings = [.. states];
        var ids = new HashSet<TState>();
        var references = new HashSet<State<AnimationStateContext<TContext>>>(ReferenceEqualityComparer.Instance);
        foreach (AnimationStateBinding<TState, TContext> binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding.Id);
            ArgumentNullException.ThrowIfNull(binding.State);
            ArgumentNullException.ThrowIfNull(binding.Timeline);
            if (!Enum.IsDefined(binding.Wrap))
            {
                throw new ArgumentOutOfRangeException(nameof(states));
            }

            if (binding.Timeline.Duration.Ticks <= 0 || !ids.Add(binding.Id) || !references.Add(binding.State))
            {
                throw new ArgumentException("State bindings must be unique and have positive timelines.", nameof(states));
            }
        }

        if (bindings.Length != control.States.Count || control.States.Any(state => !references.Contains(state)))
        {
            throw new ArgumentException("Every control state needs exactly one animation binding.", nameof(states));
        }

        Control = control;
        AutomaticTrigger = automaticTrigger;
        Bindings = bindings;
        InitialState = bindings.Single(binding => ReferenceEquals(binding.State, control.InitialState)).Id;
    }

    /// <summary>Gets the control.</summary>
    public StateMachine<AnimationStateContext<TContext>, StateMachineTrigger> Control { get; }

    /// <summary>Gets the initial state.</summary>
    public TState InitialState { get; }

    /// <summary>Gets the automatic trigger.</summary>
    public StateMachineTrigger? AutomaticTrigger { get; }

    internal AnimationStateBinding<TState, TContext>[] Bindings { get; }
}
