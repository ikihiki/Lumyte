using Lumyte.StateMachines;

namespace Lumyte.Animation;

/// <summary>Compiles animation bindings and generic control definitions without starting playback.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
public sealed class AnimationStateMachineBuilder<TState, TContext>
    where TState : notnull
{
    private readonly List<PendingState> _states = [];
    private readonly List<PendingTransition> _transitions = [];

    /// <summary>Registers a state for subsequent definition compilation.</summary>
    /// <param name="id">The id.</param>
    /// <param name="timeline">The timeline.</param>
    /// <param name="wrap">The wrap.</param>
    public void AddState(TState id, AnimationTimeline timeline, AnimationWrapMode wrap = AnimationWrapMode.Once)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(timeline);
        _states.Add(new PendingState(id, timeline, null, wrap));
    }

    /// <summary>Registers a state for subsequent definition compilation.</summary>
    /// <param name="id">The id.</param>
    /// <param name="timeline">The timeline.</param>
    /// <param name="wrap">The wrap.</param>
    public void AddState(TState id, ComposeAnimation.Definitions.Timeline timeline, AnimationWrapMode wrap = AnimationWrapMode.Once)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(timeline);
        _states.Add(new PendingState(id, null, timeline, wrap));
    }

    /// <summary>Registers an ordered transition for subsequent definition compilation.</summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="condition">The condition.</param>
    /// <param name="trigger">The trigger.</param>
    /// <param name="onCompleted">The on completed.</param>
    /// <param name="priority">The priority.</param>
    public void AddTransition(TState from, TState to, Func<TContext, AnimationStateInfo, bool>? condition = null, StateMachineTrigger? trigger = null, bool onCompleted = false, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        _transitions.Add(new PendingTransition(from, to, condition, trigger, onCompleted, priority));
    }

    /// <summary>Validates and snapshots the definition without starting playback or applying values.</summary>
    /// <param name="initialState">The initial state.</param>
    /// <returns>The computed result.</returns>
    public AnimationStateMachineDefinition<TState, TContext> Build(TState initialState)
    {
        ArgumentNullException.ThrowIfNull(initialState);
        var states = new Dictionary<TState, AnimationStateBinding<TState, TContext>>();
        foreach (PendingState state in _states)
        {
            if (!Enum.IsDefined(state.Wrap))
            {
                throw new ArgumentOutOfRangeException(nameof(state.Wrap));
            }

            string? name = state.Id.ToString();
            var controlState = new State<AnimationStateContext<TContext>>(string.IsNullOrWhiteSpace(name) ? $"State{states.Count}" : name);
            AnimationTimeline timeline = state.Timeline ?? state.Composition!.Build();
            if (!states.TryAdd(state.Id, new AnimationStateBinding<TState, TContext>(state.Id, controlState, timeline, state.Wrap)))
            {
                throw new ArgumentException("A state identifier cannot be registered twice.");
            }
        }

        if (!states.TryGetValue(initialState, out AnimationStateBinding<TState, TContext> initial))
        {
            throw new ArgumentException("The initial state must be registered.", nameof(initialState));
        }

        var automatic = StateMachineTrigger.Create();
        var builder = new StateMachineBuilder<AnimationStateContext<TContext>, StateMachineTrigger>(initial.State);
        foreach (AnimationStateBinding<TState, TContext> binding in states.Values)
        {
            builder.AddState(binding.State);
        }

        foreach (PendingTransition item in _transitions)
        {
            if (!states.TryGetValue(item.From, out AnimationStateBinding<TState, TContext> from) || !states.TryGetValue(item.To, out AnimationStateBinding<TState, TContext> to))
            {
                throw new ArgumentException("Transition endpoints must be registered.");
            }

            if (item.OnCompleted && from.Wrap == AnimationWrapMode.Loop)
            {
                throw new ArgumentException("A looping state cannot have a completion transition.");
            }

            Transition<AnimationStateContext<TContext>, StateMachineTrigger> transition = new Transition<AnimationStateContext<TContext>, StateMachineTrigger>(from.State, to.State, item.Trigger ?? automatic).WithPriority(item.Priority);
            if (item.OnCompleted)
            {
                transition.When(context => context.Playback.IsCompleted);
            }

            if (item.Condition is not null)
            {
                transition.When(context => item.Condition(context.Input, context.Playback));
            }

            builder.AddTransition(transition);
        }

        return new AnimationStateMachineDefinition<TState, TContext>(builder.BuildDefinition(), [.. states.Values], automatic);
    }

    private readonly record struct PendingState(TState Id, AnimationTimeline? Timeline, ComposeAnimation.Definitions.Timeline? Composition, AnimationWrapMode Wrap);

    private readonly record struct PendingTransition(TState From, TState To, Func<TContext, AnimationStateInfo, bool>? Condition, StateMachineTrigger? Trigger, bool OnCompleted, int Priority);
}
