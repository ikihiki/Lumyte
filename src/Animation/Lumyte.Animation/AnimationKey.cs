using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation key.</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <param name="Time">The time.</param>
/// <param name="Value">The value.</param>
public readonly record struct AnimationKey<T>(Duration Time, T Value)
{
    /// <summary>Gets a value indicating whether this value is held until the next exact key time.</summary>
    public bool Hold { get; init; }

    /// <summary>Gets the outgoing segment interpolator, or null to use the curve default.</summary>
    public IAnimationInterpolator<T>? Interpolator { get; init; }

    /// <summary>Gets outgoing temporal easing, or null for linear progress.</summary>
    public IAnimationTiming? Timing { get; init; }
}
