using System.Numerics;

namespace Lumyte.Graphics.Text.Tests;

public sealed class TextRenderingPolicyTests
{
    [Theory]
    [InlineData(18, TextRenderingMode.Coverage)]
    [InlineData(19, TextRenderingMode.SignedDistance)]
    [InlineData(48, TextRenderingMode.SignedDistance)]
    [InlineData(49, TextRenderingMode.MultiChannelSignedDistance)]
    [InlineData(96, TextRenderingMode.MultiChannelSignedDistance)]
    [InlineData(97, TextRenderingMode.Polygon)]
    [InlineData(256, TextRenderingMode.Polygon)]
    [InlineData(257, TextRenderingMode.VectorPath)]
    public void DefaultPolicySelectsRouteFromFontSize(float size, TextRenderingMode expected)
    {
        TextRenderingMode actual = TextRenderingPolicy.Default.Select(size);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SelectionUsesOnScreenTransformAndDeviceScale()
    {
        var transform = Matrix3x2.CreateScale(2) * Matrix3x2.CreateRotation(0.3f);

        TextRenderingMode actual = TextRenderingPolicy.Default.Select(24, transform, 2);

        Assert.Equal(TextRenderingMode.MultiChannelSignedDistance, actual);
    }

    [Fact]
    public void SelectionUsesLargestScaleForNonUniformTransform()
    {
        var transform = Matrix3x2.CreateScale(1, 5);

        TextRenderingMode actual = TextRenderingPolicy.Default.Select(20, transform);

        Assert.Equal(TextRenderingMode.Polygon, actual);
    }

    [Fact]
    public void CustomPolicyControlsEveryBoundary()
    {
        var policy = new TextRenderingPolicy
        {
            CoverageMaximumSize = 10,
            SignedDistanceMaximumSize = 20,
            MultiChannelSignedDistanceMaximumSize = 30,
            PolygonMaximumSize = 40,
        };

        TextRenderingMode[] actual = [
            policy.Select(10),
            policy.Select(20),
            policy.Select(30),
            policy.Select(40),
            policy.Select(41),
        ];

        Assert.Equal([
            TextRenderingMode.Coverage,
            TextRenderingMode.SignedDistance,
            TextRenderingMode.MultiChannelSignedDistance,
            TextRenderingMode.Polygon,
            TextRenderingMode.VectorPath,
        ], actual);
    }

    [Fact]
    public void DescendingBoundariesAreRejected()
    {
        var policy = new TextRenderingPolicy
        {
            CoverageMaximumSize = 20,
            SignedDistanceMaximumSize = 10,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => policy.Select(12));

        Assert.Contains("ascending", exception.Message, StringComparison.Ordinal);
    }
}
