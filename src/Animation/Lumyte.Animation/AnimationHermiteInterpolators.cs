using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Mathematics;

namespace Lumyte.Animation;

/// <summary>Provides component-wise cubic Hermite interpolation with per-second tangents.</summary>
public static class AnimationHermiteInterpolators
{
    /// <summary>Gets Float Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<float> Float { get; } = new Interpolator<float>(HermiteInterpolation.Interpolate);

    /// <summary>Gets Vector2 Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Vector2> Vector2 { get; } = new Interpolator<Vector2>(HermiteInterpolation.Interpolate);

    /// <summary>Gets Vector3 Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Vector3> Vector3 { get; } = new Interpolator<Vector3>(HermiteInterpolation.Interpolate);

    /// <summary>Gets Vector4 Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Vector4> Vector4 { get; } = new Interpolator<Vector4>(HermiteInterpolation.Interpolate);

    /// <summary>Gets Quaternion Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Quaternion> Quaternion { get; } = new Interpolator<Quaternion>(HermiteInterpolation.Interpolate);

    private sealed class Interpolator<T>(Func<T, T, T, T, double, float, T> interpolate) : IAnimationHermiteInterpolator<T>
    {
        /// <inheritdoc />
        public T Interpolate(T from, T to, T outgoingTangent, T incomingTangent, Duration interval, float amount)
        {
            AnimationSourceValidation.Duration(interval);
            AnimationTimings.ValidateAmount(amount);
            AnimationInterpolators.Validate(from);
            AnimationInterpolators.Validate(to);
            AnimationInterpolators.Validate(outgoingTangent, normalizedRotation: false);
            AnimationInterpolators.Validate(incomingTangent, normalizedRotation: false);
            if (amount == 0)
            {
                return from;
            }

            if (amount == 1)
            {
                return to;
            }

            return interpolate(from, to, outgoingTangent, incomingTangent, interval.TotalSeconds, amount);
        }
    }
}
