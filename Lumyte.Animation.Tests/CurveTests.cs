using Xunit;

namespace Lumyte.Animation.Tests;

public sealed class CurveTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void LinearCurvePreservesProgress(float progress)
    {
        var result = Curves.Linear.Transform(progress);

        Assert.Equal(progress, result);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(2f, 1f)]
    public void CubicBezierCurveClampsItsEndpoints(float progress, float expected)
    {
        var result = Curves.EaseInOut.Transform(progress);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void EaseOutAdvancesFasterThanLinearAtItsMidpoint()
    {
        var result = Curves.EaseOut.Transform(0.5f);

        Assert.True(result > 0.5f, $"Expected eased progress above 0.5, but found {result}.");
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.24f, 0f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(0.99f, 0.75f)]
    [InlineData(1f, 1f)]
    public void EndStepsHoldEachValueUntilTheNextBoundary(float progress, float expected)
    {
        var curve = new StepsCurve(4);

        var result = curve.Transform(progress);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(StepPosition.JumpStart, 0f, 0.25f)]
    [InlineData(StepPosition.JumpStart, 1f, 1f)]
    [InlineData(StepPosition.JumpBoth, 0f, 0.2f)]
    [InlineData(StepPosition.JumpBoth, 1f, 1f)]
    [InlineData(StepPosition.JumpNone, 0f, 0f)]
    [InlineData(StepPosition.JumpNone, 1f, 1f)]
    public void StepPositionsDefineStableTimelineEndpoints(
        StepPosition position,
        float progress,
        float expected)
    {
        var curve = new StepsCurve(4, position);

        var result = curve.Transform(progress);

        Assert.Equal(expected, result);
    }
}
