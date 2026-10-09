using Lumyte.StateMachines;

namespace Lumyte.Animation;

/// <summary>Represents animation state binding.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <typeparam name="TContext">The application input and callback context type.</typeparam>
/// <param name="Id">The id.</param>
/// <param name="State">The state.</param>
/// <param name="Timeline">The timeline.</param>
/// <param name="Wrap">The wrap.</param>
public readonly record struct AnimationStateBinding<TState, TContext>(TState Id, State<AnimationStateContext<TContext>> State, AnimationTimeline Timeline, AnimationWrapMode Wrap = AnimationWrapMode.Once)
    where TState : notnull;
