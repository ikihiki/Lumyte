using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Interpolates values and per-second tangents over a typed interval.</summary>
/// <typeparam name="T">The value and tangent type.</typeparam>
public interface IAnimationHermiteInterpolator<T>
{
    /// <summary>Evaluates cubic Hermite interpolation using tangents scaled by interval seconds.</summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="outgoingTangent">The starting derivative per second.</param>
    /// <param name="incomingTangent">The ending derivative per second.</param>
    /// <param name="interval">The positive key interval.</param>
    /// <param name="amount">The finite normalized progress in [0, 1].</param>
    /// <returns>The interpolated value.</returns>
    T Interpolate(T from, T to, T outgoingTangent, T incomingTangent, Duration interval, float amount);
}
