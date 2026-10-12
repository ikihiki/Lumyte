namespace Lumyte.Animation;

/// <summary>Transforms normalized time independently of value interpolation.</summary>
public interface IAnimationTiming
{
    /// <summary>Maps finite time in [0, 1] to a finite progress, permitting overshoot.</summary>
    /// <param name="amount">The normalized time.</param>
    /// <returns>The transformed progress.</returns>
    double Transform(double amount);
}
