using System.Numerics;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Verifies interpolation invariants outside the unit interval.</summary>
public sealed class ExtrapolationTests
{
    /// <summary>Preserves constant scalars and vector components without cancelling large weights.</summary>
    /// <param name="amount">The finite extrapolation parameter.</param>
    [Theory]
    [InlineData(-float.MaxValue)]
    [InlineData(-1e20f)]
    [InlineData(-1e8f)]
    [InlineData(1e8f)]
    [InlineData(1e20f)]
    [InlineData(float.MaxValue)]
    public void ConstantValuesSurviveExtremeExtrapolation(float amount)
    {
        foreach (float value in new[] { 1f, -1f, float.Epsilon, float.MaxValue, float.MinValue })
        {
            Assert.Equal(value, Interpolation.Linear(value, value, amount));
        }

        var vector2 = new Vector2(float.MaxValue, float.Epsilon);
        var vector3 = new Vector3(float.MinValue, 1, float.Epsilon);
        var vector4 = new Vector4(float.MinValue, 1, float.Epsilon, float.MaxValue);
        Assert.Equal(vector2, Interpolation.Linear(vector2, vector2, amount));
        Assert.Equal(vector3, Interpolation.Linear(vector3, vector3, amount));
        Assert.Equal(vector4, Interpolation.Linear(vector4, vector4, amount));

        Assert.Equal(Quaternion.Identity, QuaternionInterpolation.Slerp(Quaternion.Identity, Quaternion.Identity, amount));
        var rotation = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        Assert.Equal(rotation, QuaternionInterpolation.Slerp(rotation, rotation, amount));
        Assert.Equal(rotation, QuaternionInterpolation.Slerp(rotation, Quaternion.Negate(rotation), amount));
    }

    /// <summary>Retains changes whose extrapolated values remain representable.</summary>
    [Fact]
    public void ScalarExtrapolationKeepsSmallEndpointDifferences()
    {
        float expected = (float)(float.Epsilon + ((double)float.MaxValue * float.Epsilon));
        Assert.Equal(expected, Interpolation.Linear(float.Epsilon, 2 * float.Epsilon, float.MaxValue));
        Assert.Equal(3, Interpolation.Linear(1, 2, 2));
        Assert.Equal(0, Interpolation.Linear(1, 2, -1));
    }

    /// <summary>Preserves existing weighted interpolation behavior at endpoints and opposite extremes.</summary>
    [Fact]
    public void VectorInterpolationPreservesEndpointsAndFiniteMidpoints()
    {
        var tiny = new Vector4(float.Epsilon);
        var largest = new Vector4(float.MaxValue);
        Assert.Equal(tiny, Interpolation.Linear(tiny, largest, 0));
        Assert.Equal(tiny, Interpolation.Linear(largest, tiny, 1));
        Assert.Equal(Vector2.Zero, Interpolation.Linear(new Vector2(float.MinValue), new Vector2(float.MaxValue), 0.5f));
        Assert.Equal(Vector3.Zero, Interpolation.Linear(new Vector3(float.MinValue), new Vector3(float.MaxValue), 0.5f));
        Assert.Equal(Vector4.Zero, Interpolation.Linear(-largest, largest, 0.5f));
    }

    /// <summary>Extrapolates small rotations spherically instead of amplifying a linear approximation.</summary>
    /// <param name="amount">The extrapolation parameter.</param>
    [Theory]
    [InlineData(-1e8f)]
    [InlineData(1e8f)]
    public void NearlyEqualRotationsFollowTheSphericalArcDuringExtrapolation(float amount)
    {
        var target = new Quaternion(0, 5e-9f, 0, 1);
        double angle = Math.Atan2(target.Y, target.W) * amount;
        Quaternion result = QuaternionInterpolation.Slerp(Quaternion.Identity, target, amount);
        Assert.Equal((float)Math.Sin(angle), result.Y, 6);
        Assert.Equal((float)Math.Cos(angle), result.W, 6);
        Assert.Equal(1, result.Length(), 6);
        Assert.Equal(result, QuaternionInterpolation.Slerp(Quaternion.Identity, Quaternion.Negate(target), amount));

        Quaternion extreme = QuaternionInterpolation.Slerp(Quaternion.Identity, target, float.MaxValue);
        Assert.Equal(1, extreme.Length(), 6);
        Assert.True(float.IsFinite(extreme.Y));
        Assert.True(float.IsFinite(extreme.W));
    }

    /// <summary>Preserves rotation composition and shortest-arc direction for ordinary extrapolation.</summary>
    /// <param name="amount">The extrapolation parameter.</param>
    [Theory]
    [InlineData(-2f)]
    [InlineData(2f)]
    [InlineData(3.5f)]
    public void QuaternionExtrapolationMatchesTheSphericalReference(float amount)
    {
        var from = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f);
        var to = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -1.2f);
        var expected = Quaternion.Normalize(Quaternion.Slerp(from, to, amount));
        Quaternion actual = QuaternionInterpolation.Slerp(from, to, amount);
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
        Assert.Equal(expected.Z, actual.Z, 5);
        Assert.Equal(expected.W, actual.W, 5);
        Assert.Equal(actual, QuaternionInterpolation.Slerp(from, Quaternion.Negate(to), amount));
    }
}
