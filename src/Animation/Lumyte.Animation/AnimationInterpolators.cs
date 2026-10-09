using System.Numerics;
using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Represents animation interpolators.</summary>
public static class AnimationInterpolators
{
    /// <summary>Gets the float.</summary>
    public static IAnimationInterpolator<float> Float { get; } = new Interpolator<float>((a, b, t) => (float)(((1d - t) * a) + ((double)t * b)));

    /// <summary>Gets the vector2.</summary>
    public static IAnimationInterpolator<Vector2> Vector2 { get; } = new Interpolator<Vector2>(System.Numerics.Vector2.Lerp);

    /// <summary>Gets the vector3.</summary>
    public static IAnimationInterpolator<Vector3> Vector3 { get; } = new Interpolator<Vector3>(System.Numerics.Vector3.Lerp);

    /// <summary>Gets the vector4.</summary>
    public static IAnimationInterpolator<Vector4> Vector4 { get; } = new Interpolator<Vector4>(System.Numerics.Vector4.Lerp);

    /// <summary>Gets the quaternion.</summary>
    public static IAnimationInterpolator<Quaternion> Quaternion { get; } = new Interpolator<Quaternion>((a, b, t) => System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Slerp(a, b, t)));

    /// <summary>Gets tick interpolation with exact endpoints and nearest-tick, ties-to-even rounding.</summary>
    public static IAnimationInterpolator<Duration> Duration { get; } = new Interpolator<Duration>(InterpolateDuration);

    /// <summary>Creates a spatial cubic Bezier interpolator using De Casteljau evaluation.</summary>
    /// <typeparam name="T">The point type.</typeparam>
    /// <param name="control1">The first control point.</param>
    /// <param name="control2">The second control point.</param>
    /// <param name="interpolator">The point interpolation rule.</param>
    /// <returns>The immutable segment interpolator.</returns>
    public static IAnimationInterpolator<T> CubicBezier<T>(T control1, T control2, IAnimationInterpolator<T> interpolator)
    {
        ArgumentNullException.ThrowIfNull(interpolator);
        Validate(control1);
        Validate(control2);
        return new Bezier<T>(control1, control2, interpolator);
    }

    /// <summary>Performs step.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <returns>The computed result.</returns>
    public static IAnimationInterpolator<T> Step<T>() => Discrete<T>.Instance;

    internal static float GetInteriorAmount(long elapsedTicks, long durationTicks, AnimationEasing easing = AnimationEasing.Linear)
    {
        double t = (double)elapsedTicks / durationTicks;
        t = easing switch
        {
            AnimationEasing.EaseIn => t * t,
            AnimationEasing.EaseOut => t * (2 - t),
            AnimationEasing.EaseInOut => t < 0.5 ? 2 * t * t : 1 - (2 * (1 - t) * (1 - t)),
            _ => t,
        };

        // Tick comparisons own the endpoints; floating-point rounding must not reach them early.
        return Math.Clamp((float)t, float.Epsilon, MathF.BitDecrement(1));
    }

    internal static void Validate<T>(T value, bool normalizedRotation = true)
    {
        if (!IsValid(value, normalizedRotation))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Standard values must be finite and rotations normalized.");
        }
    }

    internal static bool IsValid<T>(T value, bool normalizedRotation = true) => value switch
    {
        float number => float.IsFinite(number),
        Vector2 v => float.IsFinite(v.X) && float.IsFinite(v.Y),
        Vector3 v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z),
        Vector4 v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W),
        Quaternion q => float.IsFinite(q.X) && float.IsFinite(q.Y) && float.IsFinite(q.Z) && float.IsFinite(q.W) && (!normalizedRotation || MathF.Abs(q.LengthSquared() - 1f) <= 0.0001f),
        _ => true,
    };

    private static Duration InterpolateDuration(Duration from, Duration to, float amount)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(float.IsFinite(amount), true, nameof(amount));
        if (amount == 0 || from == to)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        decimal weight = (decimal)(double)amount;
        decimal ticks = decimal.Round(((1 - weight) * from.Ticks) + (weight * to.Ticks), 0, MidpointRounding.ToEven);
        return Core.Time.Duration.FromTicks(checked((long)ticks));
    }

    private sealed class Interpolator<T>(Func<T, T, float, T> interpolate) : IAnimationInterpolator<T>
    {
        /// <summary>Interpolates two values using the normalized amount.</summary>
        /// <param name="from">The from.</param>
        /// <param name="to">The to.</param>
        /// <param name="amount">The amount.</param>
        /// <returns>The computed result.</returns>
        public T Interpolate(T from, T to, float amount) => interpolate(from, to, amount);
    }

    private sealed class Bezier<T>(T control1, T control2, IAnimationInterpolator<T> interpolator) : IAnimationInterpolator<T>
    {
        /// <inheritdoc />
        public T Interpolate(T from, T to, float amount)
        {
            if (amount == 0)
            {
                return from;
            }

            if (amount == 1)
            {
                return to;
            }

            T a = interpolator.Interpolate(from, control1, amount);
            T b = interpolator.Interpolate(control1, control2, amount);
            T c = interpolator.Interpolate(control2, to, amount);
            return interpolator.Interpolate(interpolator.Interpolate(a, b, amount), interpolator.Interpolate(b, c, amount), amount);
        }
    }

    private sealed class Discrete<T> : IAnimationInterpolator<T>
    {
        internal static readonly Discrete<T> Instance = new();

        /// <summary>Interpolates two values using the normalized amount.</summary>
        /// <param name="from">The from.</param>
        /// <param name="to">The to.</param>
        /// <param name="amount">The amount.</param>
        /// <returns>The computed result.</returns>
        public T Interpolate(T from, T to, float amount) => amount < 1 ? from : to;
    }
}
