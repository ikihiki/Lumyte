using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation event.</summary>
/// <param name="Time">The time.</param>
/// <param name="Name">The name.</param>
/// <param name="Payload">The payload.</param>
public readonly record struct AnimationEvent(Duration Time, string Name, string? Payload);
