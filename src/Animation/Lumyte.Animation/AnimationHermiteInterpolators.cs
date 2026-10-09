using System.Numerics;
using Lumyte.Core.Time;

namespace Lumyte.Animation;

/// <summary>Provides component-wise cubic Hermite interpolation with per-second tangents.</summary>
public static class AnimationHermiteInterpolators
{
    /// <summary>Gets scalar Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<float> Float { get; } = new Interpolator<float>((a, b, outgoing, incoming, weights) => Component(a, b, outgoing, incoming, weights));

    /// <summary>Gets two-dimensional Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Vector2> Vector2 { get; } = new Interpolator<Vector2>((a, b, outgoing, incoming, w) => new(
        Component(a.X, b.X, outgoing.X, incoming.X, w), Component(a.Y, b.Y, outgoing.Y, incoming.Y, w)));

    /// <summary>Gets three-dimensional Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Vector3> Vector3 { get; } = new Interpolator<Vector3>((a, b, outgoing, incoming, w) => new(
        Component(a.X, b.X, outgoing.X, incoming.X, w), Component(a.Y, b.Y, outgoing.Y, incoming.Y, w), Component(a.Z, b.Z, outgoing.Z, incoming.Z, w)));

    /// <summary>Gets four-dimensional Hermite interpolation.</summary>
    public static IAnimationHermiteInterpolator<Vector4> Vector4 { get; } = new Interpolator<Vector4>((a, b, outgoing, incoming, w) => new(
        Component(a.X, b.X, outgoing.X, incoming.X, w), Component(a.Y, b.Y, outgoing.Y, incoming.Y, w), Component(a.Z, b.Z, outgoing.Z, incoming.Z, w), Component(a.W, b.W, outgoing.W, incoming.W, w)));

    /// <summary>Gets component-wise Hermite rotation interpolation followed by normalization.</summary>
    public static IAnimationHermiteInterpolator<Quaternion> Quaternion { get; } = new Interpolator<Quaternion>(Rotation);

    private static float Component(float a, float b, float outgoing, float incoming, Weights weights)
    {
        float result = (float)WeightedComponent(a, b, outgoing, incoming, weights);
        if (!float.IsFinite(result))
        {
            throw new InvalidOperationException("Hermite interpolation produced a non-finite component.");
        }

        return result;
    }

    private static Quaternion Rotation(Quaternion a, Quaternion b, Quaternion outgoing, Quaternion incoming, Weights weights)
    {
        double x = WeightedComponent(a.X, b.X, outgoing.X, incoming.X, weights);
        double y = WeightedComponent(a.Y, b.Y, outgoing.Y, incoming.Y, weights);
        double z = WeightedComponent(a.Z, b.Z, outgoing.Z, incoming.Z, weights);
        double w = WeightedComponent(a.W, b.W, outgoing.W, incoming.W, weights);
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
        if (length == 0)
        {
            throw new InvalidOperationException("A zero-length interpolated rotation cannot be normalized.");
        }

        return new Quaternion((float)(x / length), (float)(y / length), (float)(z / length), (float)(w / length));
    }

    private static double WeightedComponent(float a, float b, float outgoing, float incoming, Weights weights) => ((weights.From * a) + (weights.To * b)) + ((weights.Outgoing * outgoing) + (weights.Incoming * incoming));

    private readonly record struct Weights(double From, double To, double Outgoing, double Incoming);

    private sealed class Interpolator<T>(Func<T, T, T, T, Weights, T> interpolate) : IAnimationHermiteInterpolator<T>
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

            double t = amount;
            double squared = t * t;
            double cubed = squared * t;
            var weights = new Weights(
                (2 * cubed) - (3 * squared) + 1,
                (-2 * cubed) + (3 * squared),
                (cubed - (2 * squared) + t) * interval.TotalSeconds,
                (cubed - squared) * interval.TotalSeconds);
            return interpolate(from, to, outgoingTangent, incomingTangent, weights);
        }
    }
}
