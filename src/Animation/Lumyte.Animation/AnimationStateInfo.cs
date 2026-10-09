using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation state info.</summary>
/// <param name="Position">The position.</param>
/// <param name="Duration">The duration.</param>
/// <param name="IsCompleted">The is completed.</param>
public readonly record struct AnimationStateInfo(Duration Position, Duration Duration, bool IsCompleted);
