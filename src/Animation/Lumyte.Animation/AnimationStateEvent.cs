using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation state event.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <param name="State">The state.</param>
/// <param name="Occurrence">The occurrence.</param>
/// <param name="OccurredAt">The marker passage time in the execution instance's clock domain.</param>
public readonly record struct AnimationStateEvent<TState>(TState State, AnimationEventOccurrence Occurrence, TimePoint OccurredAt)
    where TState : notnull;
