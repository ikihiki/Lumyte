using System.Numerics;

namespace Lumyte.Graphics.TwoD.Tests;

public sealed class ColorPaintPrimitiveTests
{
    [Fact]
    public void LinearGradientCopiesSortsAndPreservesEveryStop()
    {
        var stops = new[]
        {
            new GradientStop(1, new(0, 0, 1)),
            new GradientStop(-0.5f, new(1, 0, 0)),
            new GradientStop(0.25f, new(0, 1, 0, 0.5f)),
        };

        Brush brush = Brush.LinearGradient(
            new(1, 2),
            new(9, 2),
            new(1, 7),
            stops,
            GradientExtendMode.Reflect);
        stops[1] = new(4, Color.White);

        Assert.Equal(BrushKind.LinearGradient, brush.Kind);
        Assert.Equal(GradientExtendMode.Reflect, brush.ExtendMode);
        Assert.Equal(new Vector2(1, 2), brush.Point0);
        Assert.Equal(new Vector2(9, 2), brush.Point1);
        Assert.Equal(new Vector2(1, 7), brush.Point2);
        Assert.Collection(
            brush.GradientStops,
            stop => Assert.Equal(new GradientStop(-0.5f, new(1, 0, 0)), stop),
            stop => Assert.Equal(new GradientStop(0.25f, new(0, 1, 0, 0.5f)), stop),
            stop => Assert.Equal(new GradientStop(1, new(0, 0, 1)), stop));
    }

    [Fact]
    public void RadialGradientPreservesBothCircles()
    {
        Brush brush = Brush.RadialGradient(
            new(2, 3),
            1,
            new(8, 9),
            12,
            [new(0, Color.White), new(1, Color.Transparent)],
            GradientExtendMode.Repeat);

        Assert.Equal(BrushKind.RadialGradient, brush.Kind);
        Assert.Equal(new Vector2(2, 3), brush.Point0);
        Assert.Equal(1, brush.Radius0);
        Assert.Equal(new Vector2(8, 9), brush.Point1);
        Assert.Equal(12, brush.Radius1);
        Assert.Equal(GradientExtendMode.Repeat, brush.ExtendMode);
    }

    [Fact]
    public void RadialGradientPreservesNegativeVariableRadii()
    {
        Brush brush = Brush.RadialGradient(
            Vector2.Zero,
            -2,
            Vector2.One,
            -1,
            [new(0, Color.White)]);

        Assert.Equal(-2, brush.Radius0);
        Assert.Equal(-1, brush.Radius1);
    }

    [Fact]
    public void SweepGradientPreservesAnglesAndStops()
    {
        Brush brush = Brush.SweepGradient(
            new(4, 5),
            -MathF.PI,
            MathF.PI,
            [new(0, new(1, 0, 0)), new(0.5f, new(0, 1, 0)), new(1, new(0, 0, 1))]);

        Assert.Equal(BrushKind.SweepGradient, brush.Kind);
        Assert.Equal(new Vector2(4, 5), brush.Point0);
        Assert.Equal(-MathF.PI, brush.StartAngle);
        Assert.Equal(MathF.PI, brush.EndAngle);
        Assert.Equal(3, brush.GradientStops.Count);
    }

    [Fact]
    public void SweepGradientPreservesEqualAngles()
    {
        Brush brush = Brush.SweepGradient(
            Vector2.Zero,
            1,
            1,
            [new(0, Color.White)],
            GradientExtendMode.Repeat);

        Assert.Equal(1, brush.StartAngle);
        Assert.Equal(1, brush.EndAngle);
    }

    [Fact]
    public void EqualGradientValuesHaveStructuralEquality()
    {
        Brush first = Brush.LinearGradient(
            Vector2.Zero,
            Vector2.UnitX,
            Vector2.UnitY,
            [new(0, Color.White), new(1, Color.Transparent)]);
        Brush second = Brush.LinearGradient(
            Vector2.Zero,
            Vector2.UnitX,
            Vector2.UnitY,
            [new(0, Color.White), new(1, Color.Transparent)]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GradientRejectsAnEmptyColorLine()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Brush.SweepGradient(Vector2.Zero, 0, 1, []));

        Assert.Equal("stops", exception.ParamName);
    }

    [Fact]
    public void CompositeModesMatchTheOpenTypeAbi()
    {
        CompositeMode[] expected =
        [
            CompositeMode.Clear,
            CompositeMode.Source,
            CompositeMode.Destination,
            CompositeMode.SourceOver,
            CompositeMode.DestinationOver,
            CompositeMode.SourceIn,
            CompositeMode.DestinationIn,
            CompositeMode.SourceOut,
            CompositeMode.DestinationOut,
            CompositeMode.SourceAtop,
            CompositeMode.DestinationAtop,
            CompositeMode.Xor,
            CompositeMode.Plus,
            CompositeMode.Screen,
            CompositeMode.Overlay,
            CompositeMode.Darken,
            CompositeMode.Lighten,
            CompositeMode.ColorDodge,
            CompositeMode.ColorBurn,
            CompositeMode.HardLight,
            CompositeMode.SoftLight,
            CompositeMode.Difference,
            CompositeMode.Exclusion,
            CompositeMode.Multiply,
            CompositeMode.HslHue,
            CompositeMode.HslSaturation,
            CompositeMode.HslColor,
            CompositeMode.HslLuminosity,
        ];

        Assert.Equal(Enumerable.Range(0, expected.Length), expected.Select(static mode => (int)mode));
        Assert.Equal(expected, Enum.GetValues<CompositeMode>());
    }

    [Theory]
    [InlineData(BlendMode.SourceOver, CompositeMode.SourceOver)]
    [InlineData(BlendMode.Additive, CompositeMode.Plus)]
    [InlineData(BlendMode.Multiply, CompositeMode.Multiply)]
    [InlineData(BlendMode.Screen, CompositeMode.Screen)]
    public void LegacyBlendModeMapsToCompositeMode(BlendMode legacy, CompositeMode expected)
    {
        var options = new LayerOptions { BlendMode = legacy };

        Assert.Equal(expected, options.CompositeMode);
    }

    [Fact]
    public void ExplicitCompositeModeTakesPrecedenceOverLegacyBlendMode()
    {
        var options = new LayerOptions
        {
            BlendMode = BlendMode.Multiply,
            CompositeMode = CompositeMode.HslHue,
        };

        Assert.Equal(CompositeMode.HslHue, options.CompositeMode);
    }
}
