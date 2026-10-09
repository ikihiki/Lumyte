using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Stores a key value and its incoming and outgoing derivatives per second.</summary>
/// <typeparam name="T">The value and tangent type.</typeparam>
/// <param name="Time">The local key time.</param>
/// <param name="Value">The value at that time.</param>
/// <param name="IncomingTangent">The derivative arriving at the key.</param>
/// <param name="OutgoingTangent">The derivative leaving the key.</param>
public readonly record struct AnimationHermiteKey<T>(Duration Time, T Value, T IncomingTangent, T OutgoingTangent);
