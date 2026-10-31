using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks color validity at creation while retaining the existing record API.</summary>
public sealed class Color2DTests
{
    /// <summary>Provides invalid values for every component.</summary>
    /// <returns>The component index and invalid value.</returns>
    public static IEnumerable<object[]> InvalidComponents()
    {
        for (int component = 0; component < 4; component++)
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0.01f, 1.01f })
            {
                yield return [component, invalid];
            }
        }
    }

    /// <summary>Checks constructors, object initializers and record copies all enforce the same color invariant.</summary>
    /// <param name="component">The component to invalidate.</param>
    /// <param name="invalid">The invalid component value.</param>
    [Theory]
    [MemberData(nameof(InvalidComponents))]
    public void InvalidColorsAreRejectedAtCreation(int component, float invalid)
    {
        float[] values = [0.25f, 0.5f, 0.75f, 1];
        values[component] = invalid;
        string name = "RGBA"[component].ToString();
        Assert.Equal(name, Assert.Throws<ArgumentOutOfRangeException>(() => new Color2D(values[0], values[1], values[2], values[3])).ParamName);

        var original = new Color2D(0.25f, 0.5f, 0.75f, 1);
        Assert.Equal(name, Assert.Throws<ArgumentOutOfRangeException>(() => CopyWith(original, component, invalid)).ParamName);
        Assert.Equal(name, Assert.Throws<ArgumentOutOfRangeException>(() => Initialize(component, invalid)).ParamName);
        Assert.Equal(new Color2D(0.25f, 0.5f, 0.75f, 1), original);
    }

    /// <summary>Checks default transparency, named arguments, deconstruction, equality and valid init/with use remain supported.</summary>
    [Fact]
    public void ValidColorsKeepTheExistingRecordApi()
    {
        Assert.Equal(new Color2D(0, 0, 0, 0), default);
        var color = new Color2D(R: 0, G: 0.25f, B: 0.5f, A: 1);
        (float r, float g, float b, float a) = color;
        Assert.Equal((0f, 0.25f, 0.5f, 1f), (r, g, b, a));
        Assert.Equal(new Color2D(1, 0.25f, 0.5f, 0), color with { R = 1, A = 0 });
        Assert.Equal(new Color2D(1, 0.5f, 0.25f, 1), new Color2D { R = 1, G = 0.5f, B = 0.25f, A = 1 });
    }

    private static Color2D CopyWith(Color2D color, int component, float value) => component switch
    {
        0 => color with { R = value },
        1 => color with { G = value },
        2 => color with { B = value },
        3 => color with { A = value },
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };

    private static Color2D Initialize(int component, float value) => component switch
    {
        0 => new Color2D { R = value },
        1 => new Color2D { G = value },
        2 => new Color2D { B = value },
        3 => new Color2D { A = value },
        _ => throw new ArgumentOutOfRangeException(nameof(component)),
    };
}
