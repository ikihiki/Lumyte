using Lumyte.Mathematics;

namespace Lumyte.Animation;

/// <summary>Provides reusable temporal easing, including inverse-x cubic Bezier curves.</summary>
public static class AnimationTimings
{
    /// <summary>Gets linear progress.</summary>
    public static IAnimationTiming Linear { get; } = new Polynomial(AnimationEasing.Linear);

    /// <summary>Gets quadratic ease-in progress.</summary>
    public static IAnimationTiming EaseIn { get; } = new Polynomial(AnimationEasing.EaseIn);

    /// <summary>Gets quadratic ease-out progress.</summary>
    public static IAnimationTiming EaseOut { get; } = new Polynomial(AnimationEasing.EaseOut);

    /// <summary>Gets symmetric quadratic easing.</summary>
    public static IAnimationTiming EaseInOut { get; } = new Polynomial(AnimationEasing.EaseInOut);

    /// <summary>Creates temporal cubic Bezier easing with finite controls and x controls in [0, 1].</summary>
    /// <param name="x1">The first horizontal control.</param>
    /// <param name="y1">The first vertical control, allowing overshoot.</param>
    /// <param name="x2">The second horizontal control.</param>
    /// <param name="y2">The second vertical control, allowing overshoot.</param>
    /// <returns>The immutable temporal curve.</returns>
    public static IAnimationTiming CubicBezier(double x1, double y1, double x2, double y2)
    {
        ValidateAmount(x1);
        ValidateAmount(x2);
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(y1), true, nameof(y1));
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(y2), true, nameof(y2));
        return new Bezier(x1, y1, x2, y2);
    }

    internal static void ValidateAmount(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(amount), true, nameof(amount));
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(amount, 1);
    }

    internal static float Apply(IAnimationTiming? timing, float amount)
    {
        if (timing is null)
        {
            return amount;
        }

        double result = timing.Transform(amount);
        if (!double.IsFinite(result) || result > float.MaxValue || result < float.MinValue)
        {
            throw new InvalidOperationException("Timing must produce finite float-representable progress.");
        }

        return (float)result;
    }

    private sealed class Polynomial(AnimationEasing easing) : IAnimationTiming
    {
        /// <inheritdoc />
        public double Transform(double amount)
        {
            ValidateAmount(amount);
            return easing switch
            {
                AnimationEasing.EaseIn => Interpolation.EaseIn(amount),
                AnimationEasing.EaseOut => Interpolation.EaseOut(amount),
                AnimationEasing.EaseInOut => Interpolation.EaseInOut(amount),
                _ => amount,
            };
        }
    }

    private sealed class Bezier(double x1, double y1, double x2, double y2) : IAnimationTiming
    {
        private readonly CubicBezierTiming _curve = new(x1, y1, x2, y2);

        /// <inheritdoc />
        public double Transform(double amount) => _curve.Transform(amount);
    }
}
