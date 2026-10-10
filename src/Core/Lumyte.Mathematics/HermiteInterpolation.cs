using System.Numerics;

namespace Lumyte.Mathematics;

/// <summary>Provides cubic Hermite interpolation with derivatives in caller-selected units.</summary>
public static class HermiteInterpolation
{
    /// <summary>Interpolates endpoints with derivative tangents scaled by the interval.</summary>
    /// <param name="from">The start value.</param>
    /// <param name="to">The end value.</param>
    /// <param name="outgoingTangent">The starting derivative.</param>
    /// <param name="incomingTangent">The ending derivative.</param>
    /// <param name="interval">The positive interval in the same units as the derivatives.</param>
    /// <param name="amount">The finite normalized parameter.</param>
    /// <returns>The interpolated value; quaternion results are normalized.</returns>
    public static float Interpolate(float from, float to, float outgoingTangent, float incomingTangent, double interval, float amount)
    {
        Weights weights = GetWeights(interval, amount);
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        return Component(from, to, outgoingTangent, incomingTangent, weights);
    }

    /// <summary>Interpolates endpoints with derivative tangents scaled by the interval.</summary>
    /// <param name="from">The start value.</param>
    /// <param name="to">The end value.</param>
    /// <param name="outgoingTangent">The starting derivative.</param>
    /// <param name="incomingTangent">The ending derivative.</param>
    /// <param name="interval">The positive interval in the same units as the derivatives.</param>
    /// <param name="amount">The finite normalized parameter.</param>
    /// <returns>The interpolated value; quaternion results are normalized.</returns>
    public static Vector2 Interpolate(Vector2 from, Vector2 to, Vector2 outgoingTangent, Vector2 incomingTangent, double interval, float amount)
    {
        Weights weights = GetWeights(interval, amount);
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        return new Vector2(Component(from.X, to.X, outgoingTangent.X, incomingTangent.X, weights), Component(from.Y, to.Y, outgoingTangent.Y, incomingTangent.Y, weights));
    }

    /// <summary>Interpolates endpoints with derivative tangents scaled by the interval.</summary>
    /// <param name="from">The start value.</param>
    /// <param name="to">The end value.</param>
    /// <param name="outgoingTangent">The starting derivative.</param>
    /// <param name="incomingTangent">The ending derivative.</param>
    /// <param name="interval">The positive interval in the same units as the derivatives.</param>
    /// <param name="amount">The finite normalized parameter.</param>
    /// <returns>The interpolated value; quaternion results are normalized.</returns>
    public static Vector3 Interpolate(Vector3 from, Vector3 to, Vector3 outgoingTangent, Vector3 incomingTangent, double interval, float amount)
    {
        Weights weights = GetWeights(interval, amount);
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        return new Vector3(Component(from.X, to.X, outgoingTangent.X, incomingTangent.X, weights), Component(from.Y, to.Y, outgoingTangent.Y, incomingTangent.Y, weights), Component(from.Z, to.Z, outgoingTangent.Z, incomingTangent.Z, weights));
    }

    /// <summary>Interpolates endpoints with derivative tangents scaled by the interval.</summary>
    /// <param name="from">The start value.</param>
    /// <param name="to">The end value.</param>
    /// <param name="outgoingTangent">The starting derivative.</param>
    /// <param name="incomingTangent">The ending derivative.</param>
    /// <param name="interval">The positive interval in the same units as the derivatives.</param>
    /// <param name="amount">The finite normalized parameter.</param>
    /// <returns>The interpolated value; quaternion results are normalized.</returns>
    public static Vector4 Interpolate(Vector4 from, Vector4 to, Vector4 outgoingTangent, Vector4 incomingTangent, double interval, float amount)
    {
        Weights weights = GetWeights(interval, amount);
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        return new Vector4(Component(from.X, to.X, outgoingTangent.X, incomingTangent.X, weights), Component(from.Y, to.Y, outgoingTangent.Y, incomingTangent.Y, weights), Component(from.Z, to.Z, outgoingTangent.Z, incomingTangent.Z, weights), Component(from.W, to.W, outgoingTangent.W, incomingTangent.W, weights));
    }

    /// <summary>Interpolates endpoints with derivative tangents scaled by the interval.</summary>
    /// <param name="from">The start value.</param>
    /// <param name="to">The end value.</param>
    /// <param name="outgoingTangent">The starting derivative.</param>
    /// <param name="incomingTangent">The ending derivative.</param>
    /// <param name="interval">The positive interval in the same units as the derivatives.</param>
    /// <param name="amount">The finite normalized parameter.</param>
    /// <returns>The interpolated value; quaternion results are normalized.</returns>
    public static Quaternion Interpolate(Quaternion from, Quaternion to, Quaternion outgoingTangent, Quaternion incomingTangent, double interval, float amount)
    {
        Weights weights = GetWeights(interval, amount);
        if (amount == 0)
        {
            return from;
        }

        if (amount == 1)
        {
            return to;
        }

        return Rotation(from, to, outgoingTangent, incomingTangent, weights);
    }

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
        return QuaternionInterpolation.Normalize(x, y, z, w);
    }

    private static double WeightedComponent(float a, float b, float outgoing, float incoming, Weights weights) => ((weights.From * a) + (weights.To * b)) + ((weights.Outgoing * outgoing) + (weights.Incoming * incoming));

    private static Weights GetWeights(double interval, float amount)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(interval), true, nameof(interval));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval);
        CubicBezierTiming.ValidateAmount(amount);
        double t = amount;
        double squared = t * t;
        double cubed = squared * t;
        return new Weights((2 * cubed) - (3 * squared) + 1, (-2 * cubed) + (3 * squared), (cubed - (2 * squared) + t) * interval, (cubed - squared) * interval);
    }

    private readonly record struct Weights(double From, double To, double Outgoing, double Incoming);
}
