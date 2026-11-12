using System.Reflection;
using Lumyte.Diagnostics.Server.Components;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks the diagnostic display keeps its percentage and robust Euclidean magnitude contract.</summary>
public sealed class InputStrengthTests
{
    /// <summary>Checks ordinary, tiny and huge finite inputs at the component formatting boundary.</summary>
    /// <param name="x">The horizontal component.</param>
    /// <param name="y">The vertical component.</param>
    /// <param name="expected">The invariant percentage.</param>
    [Theory]
    [InlineData(0.3, 0.4, "50%")]
    [InlineData(0, 0, "0%")]
    [InlineData(1e308, 1e308, "100%")]
    [InlineData(1e-300, 1e-300, "0%")]
    public void FormatsEuclideanStrength(double x, double y, string expected)
    {
        Type pointType = typeof(InputPage).GetNestedType("PointModel", BindingFlags.NonPublic)!;
        object point = Activator.CreateInstance(pointType, x, y)!;
        MethodInfo strength = typeof(InputPage).GetMethod("Strength", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(expected, strength.Invoke(null, [point]));
    }
}
