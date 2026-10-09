using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation event occurrence.</summary>
/// <param name="Event">The event.</param>
/// <param name="LoopIndex">The loop index.</param>
/// <param name="UpdateOffset">The update offset.</param>
public readonly record struct AnimationEventOccurrence(AnimationEvent Event, long LoopIndex, Duration UpdateOffset);
