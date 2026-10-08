namespace Lumyte.Animation;

/// <summary>Interpolates two typed values using a normalized amount.</summary>
/// <typeparam name="T">The value type.</typeparam>
public interface IAnimationInterpolator<T>
{
    /// <summary>Interpolates two values using the normalized amount.</summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="amount">The amount.</param>
    /// <returns>The computed result.</returns>
    T Interpolate(T from, T to, float amount);
}
