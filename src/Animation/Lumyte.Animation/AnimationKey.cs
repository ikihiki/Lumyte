using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation key.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Time">The time.</param>
/// <param name="Value">The value.</param>
public readonly record struct AnimationKey<T>(Duration Time, T Value);
