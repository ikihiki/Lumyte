using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation state transition.</summary>
/// <typeparam name="TState">The external state identifier type.</typeparam>
/// <param name="From">The from.</param>
/// <param name="To">The to.</param>
/// <param name="ObservedAt">The observed at.</param>
public readonly record struct AnimationStateTransition<TState>(TState From, TState To, TimePoint ObservedAt)
    where TState : notnull;
