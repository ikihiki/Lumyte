namespace Lumyte.Animation;

/// <summary>Represents animation state event.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <param name="State">The state.</param>
/// <param name="Occurrence">The occurrence.</param>
public readonly record struct AnimationStateEvent<TState>(TState State, AnimationEventOccurrence Occurrence)
    where TState : notnull;
